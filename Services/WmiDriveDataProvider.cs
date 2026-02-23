using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using DriveFlip.Constants;
using Microsoft.Win32.SafeHandles;

namespace DriveFlip.Services;

[SupportedOSPlatform("windows")]
public class WmiDriveDataProvider : IDriveDataProvider
{
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(15);

    // ── P/Invoke ──

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        ref SENDCMDINPARAMS lpInBuffer, int nInBufferSize,
        byte[] lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, int nInBufferSize,
        byte[] lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct IDEREGS
    {
        public byte bFeaturesReg;
        public byte bSectorCountReg;
        public byte bSectorNumberReg;
        public byte bCylLowReg;
        public byte bCylHighReg;
        public byte bDriveHeadReg;
        public byte bCommandReg;
        public byte bReserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SENDCMDINPARAMS
    {
        public int cBufferSize;
        public IDEREGS irDriveRegs;
        public byte bDriveNumber;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] bReserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] dwReserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] bBuffer;
    }

    // ── Helpers ──

    private static ManagementScope CreateStorageScope()
    {
        var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
        scope.Options.Timeout = WmiTimeout;
        scope.Connect();
        return scope;
    }

    private static object? SafeValue(object? value)
    {
        if (value == null) return null;
        if (value is bool b) return b;
        if (value is Array arr)
        {
            var list = new List<object?>();
            foreach (var item in arr) list.Add(item?.ToString());
            return list;
        }
        try { return Convert.ToInt64(value); } catch { }
        return value.ToString();
    }

    private static Dictionary<string, object?> ToPropertyBag(ManagementBaseObject obj)
    {
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.Properties)
            bag[prop.Name] = SafeValue(prop.Value);
        return bag;
    }

    private SafeFileHandle OpenDrive(int deviceNumber, uint access = GENERIC_READ)
    {
        return CreateFile(
            $"\\\\.\\PhysicalDrive{deviceNumber}",
            access,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    }

    private byte[]? ReadSmartBuffer(SafeFileHandle handle, int deviceNumber, byte feature)
    {
        var cmdIn = new SENDCMDINPARAMS
        {
            cBufferSize = Smart.AttrBufferSize,
            irDriveRegs = new IDEREGS
            {
                bFeaturesReg = feature,
                bSectorCountReg = 1,
                bSectorNumberReg = 1,
                bCylLowReg = Ata.CylLow,
                bCylHighReg = Ata.CylHigh,
                bDriveHeadReg = (byte)(0xA0 | ((deviceNumber & 1) << 4)),
                bCommandReg = Ata.CmdSmart
            },
            bDriveNumber = (byte)deviceNumber,
            bReserved = new byte[3],
            dwReserved = new byte[4],
            bBuffer = new byte[1]
        };

        int inSize = Marshal.SizeOf<SENDCMDINPARAMS>();
        int outSize = Smart.HeaderSize + Smart.AttrBufferSize;
        var outBuffer = new byte[outSize];

        bool ok = DeviceIoControl(handle, Ioctl.SmartRcvDriveData,
            ref cmdIn, inSize, outBuffer, outSize, out _, IntPtr.Zero);

        return ok ? outBuffer : null;
    }

    // ── WMI Interface Implementations ──

    public List<Dictionary<string, object?>> GetWin32DiskDrives()
    {
        var results = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (var disk in searcher.Get())
                results.Add(ToPropertyBag(disk));
        }
        catch (Exception ex)
        {
            Logger.Error("GetWin32DiskDrives failed", ex);
        }
        return results;
    }

    public Dictionary<string, object?>? GetPhysicalDisk(int deviceNumber)
    {
        try
        {
            var scope = CreateStorageScope();
            var query = new ObjectQuery(
                $"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceNumber}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (var disk in searcher.Get())
                return ToPropertyBag(disk);
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetPhysicalDisk failed for disk {deviceNumber}: {ex.Message}");
        }
        return null;
    }

    public Dictionary<string, object?>? GetReliabilityCounters(int deviceNumber)
    {
        try
        {
            var scope = CreateStorageScope();
            var query = new ObjectQuery(
                $"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceNumber}'");
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject disk in searcher.Get())
            {
                try
                {
                    var related = disk.GetRelated("MSFT_StorageReliabilityCounter");
                    foreach (var counter in related)
                        return ToPropertyBag(counter);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"GetReliabilityCounters relationship query failed for disk {deviceNumber}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetReliabilityCounters failed for disk {deviceNumber}: {ex.Message}");
        }
        return null;
    }

    public (List<string> Partitions, List<string> DriveLetters) GetDriveLettersWin32(string devicePath)
    {
        var partitions = new List<string>();
        var driveLetters = new List<string>();
        try
        {
            var escapedPath = devicePath.Replace("\\", "\\\\");
            using var partSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{escapedPath}'}} " +
                "WHERE AssocClass=Win32_DiskDriveToDiskPartition");

            foreach (var partition in partSearcher.Get())
            {
                var partId = partition["DeviceID"]?.ToString() ?? "";
                partitions.Add(partId);

                using var logicalSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} " +
                    "WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (var logical in logicalSearcher.Get())
                {
                    var letter = logical["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(letter))
                        driveLetters.Add(letter);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetDriveLettersWin32 failed for {devicePath}: {ex.Message}");
        }
        return (partitions, driveLetters);
    }

    public (List<string> Partitions, List<string> DriveLetters) GetDriveLettersMsft(int deviceNumber)
    {
        var partitions = new List<string>();
        var driveLetters = new List<string>();
        try
        {
            var scope = CreateStorageScope();
            var query = new ObjectQuery(
                $"SELECT * FROM MSFT_Disk WHERE {Wmi.MsftDisk.Number} = '{deviceNumber}'");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject msftDisk in searcher.Get())
            {
                var related = msftDisk.GetRelated("MSFT_Partition");
                foreach (ManagementObject partition in related)
                {
                    var partNum = partition[Wmi.MsftPartition.PartitionNumber]?.ToString();
                    if (!string.IsNullOrEmpty(partNum))
                        partitions.Add($"Disk #{deviceNumber}, Partition #{partNum}");

                    var letterObj = partition[Wmi.MsftPartition.DriveLetter];
                    if (letterObj != null)
                    {
                        var letter = Convert.ToChar(letterObj);
                        if (letter != '\0')
                            driveLetters.Add($"{letter}:");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetDriveLettersMsft failed for disk {deviceNumber}: {ex.Message}");
        }
        return (partitions, driveLetters);
    }

    public int? GetPartitionStyle(int deviceNumber)
    {
        try
        {
            var scope = CreateStorageScope();
            var query = new ObjectQuery(
                $"SELECT {Wmi.MsftDisk.PartitionStyle} FROM MSFT_Disk WHERE {Wmi.MsftDisk.Number} = '{deviceNumber}'");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (var disk in searcher.Get())
            {
                try { return Convert.ToInt32(disk[Wmi.MsftDisk.PartitionStyle] ?? 0); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetPartitionStyle failed for disk {deviceNumber}: {ex.Message}");
        }
        return null;
    }

    public (string Vid, string Pid)? GetUsbBridgeVidPid(string pnpDeviceId)
    {
        if (string.IsNullOrEmpty(pnpDeviceId) ||
            !pnpDeviceId.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var parts = pnpDeviceId.Split('\\');
            if (parts.Length < 3) return null;
            var serial = Regex.Replace(parts[^1], @"&\d+$", "");
            if (string.IsNullOrEmpty(serial)) return null;

            var escapedSerial = serial.Replace("\\", "\\\\");
            using var searcher = new ManagementObjectSearcher(
                $"SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID_%\\\\{escapedSerial}'");

            foreach (var entity in searcher.Get())
            {
                var deviceId = entity["DeviceID"]?.ToString() ?? "";
                var match = Regex.Match(deviceId, @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})");
                if (match.Success)
                    return (match.Groups[1].Value.ToUpper(), match.Groups[2].Value.ToUpper());
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetUsbBridgeVidPid failed: {ex.Message}");
        }
        return null;
    }

    // ── IOCTL Interface Implementations ──

    public byte[]? GetAtaIdentifyViaSat(int deviceNumber)
    {
        try
        {
            using var handle = OpenDrive(deviceNumber, GENERIC_READ | GENERIC_WRITE);
            if (handle.IsInvalid) return null;

            bool is64 = IntPtr.Size == 8;
            int ofsDataBufOffset = is64 ? 24 : 20;
            int ofsSenseInfoOffset = ofsDataBufOffset + IntPtr.Size;
            int ofsCdb = ofsSenseInfoOffset + 4;
            int sptSize = ((ofsCdb + 16) + (IntPtr.Size - 1)) & ~(IntPtr.Size - 1);

            int senseOffset = (sptSize + 3) & ~3;
            int dataOffset = (senseOffset + 32 + 3) & ~3;
            int totalSize = dataOffset + 512;

            var buf = new byte[totalSize];

            BitConverter.GetBytes((ushort)sptSize).CopyTo(buf, 0);
            buf[6] = 12;   buf[7] = 32;   buf[8] = 1;
            BitConverter.GetBytes((uint)512).CopyTo(buf, 12);
            BitConverter.GetBytes((uint)10).CopyTo(buf, 16);

            if (is64)
                BitConverter.GetBytes((ulong)dataOffset).CopyTo(buf, ofsDataBufOffset);
            else
                BitConverter.GetBytes((uint)dataOffset).CopyTo(buf, ofsDataBufOffset);

            BitConverter.GetBytes((uint)senseOffset).CopyTo(buf, ofsSenseInfoOffset);

            buf[ofsCdb + 0] = Sat.OpCode;
            buf[ofsCdb + 1] = Sat.ProtocolPioIn;
            buf[ofsCdb + 2] = Sat.TransferFlags;
            buf[ofsCdb + 4] = 1;
            buf[ofsCdb + 9] = Ata.CmdIdentifyDevice;

            bool ok = DeviceIoControl(handle, Ioctl.ScsiPassThrough,
                buf, totalSize, buf, totalSize, out _, IntPtr.Zero);

            if (!ok) return null;
            if (buf[2] != 0) return null;

            ushort word0 = BitConverter.ToUInt16(buf, dataOffset);
            if (word0 == 0) return null;

            var result = new byte[512];
            Array.Copy(buf, dataOffset, result, 0, 512);
            return result;
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetAtaIdentifyViaSat failed for disk {deviceNumber}: {ex.Message}");
            return null;
        }
    }

    public byte[]? GetAtaIdentifyViaSmart(int deviceNumber)
    {
        try
        {
            using var handle = OpenDrive(deviceNumber, GENERIC_READ | GENERIC_WRITE);
            if (handle.IsInvalid) return null;

            var cmdIn = new SENDCMDINPARAMS
            {
                cBufferSize = Smart.AttrBufferSize,
                irDriveRegs = new IDEREGS
                {
                    bFeaturesReg = 0,
                    bSectorCountReg = 1,
                    bSectorNumberReg = 0,
                    bCylLowReg = 0,
                    bCylHighReg = 0,
                    bDriveHeadReg = (byte)(0xA0 | ((deviceNumber & 1) << 4)),
                    bCommandReg = Ata.CmdIdentifyDevice
                },
                bDriveNumber = (byte)deviceNumber,
                bReserved = new byte[3],
                dwReserved = new byte[4],
                bBuffer = new byte[1]
            };

            int inSize = Marshal.SizeOf<SENDCMDINPARAMS>();
            int outSize = Smart.HeaderSize + Smart.AttrBufferSize;
            var outBuffer = new byte[outSize];

            bool ok = DeviceIoControl(handle, Ioctl.SmartRcvDriveData,
                ref cmdIn, inSize, outBuffer, outSize, out _, IntPtr.Zero);

            if (!ok) return null;

            var result = new byte[Smart.AttrBufferSize];
            Array.Copy(outBuffer, Smart.HeaderSize, result, 0, Smart.AttrBufferSize);

            ushort word0 = BitConverter.ToUInt16(result, 0);
            return word0 == 0 ? null : result;
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetAtaIdentifyViaSmart failed for disk {deviceNumber}: {ex.Message}");
            return null;
        }
    }

    public (byte[]? Attributes, byte[]? Thresholds) GetSmartData(int deviceNumber)
    {
        try
        {
            using var handle = OpenDrive(deviceNumber, GENERIC_READ | GENERIC_WRITE);
            if (handle.IsInvalid) return (null, null);

            byte[]? attrBuffer = ReadSmartBuffer(handle, deviceNumber, Ata.SmartReadData);
            byte[]? threshBuffer = ReadSmartBuffer(handle, deviceNumber, Ata.SmartReadThresholds);

            return (attrBuffer, threshBuffer);
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetSmartData failed for disk {deviceNumber}: {ex.Message}");
            return (null, null);
        }
    }

    public byte[]? GetNvmeHealthLog(int deviceNumber)
    {
        try
        {
            using var handle = OpenDrive(deviceNumber);
            if (handle.IsInvalid) return null;

            int protocolDataOffset = NVMe.ProtocolDataStructSize;
            int protocolDataLength = NVMe.HealthLogSize;
            int querySize = 8 + protocolDataOffset;

            var inBuffer = new byte[querySize];

            BitConverter.GetBytes(NVMe.PropertyIdProtocolSpecific).CopyTo(inBuffer, 0);
            BitConverter.GetBytes(0).CopyTo(inBuffer, 4);

            int spsdOffset = 8;
            BitConverter.GetBytes(NVMe.ProtocolTypeNvme).CopyTo(inBuffer, spsdOffset + 0);
            BitConverter.GetBytes(NVMe.DataTypeLogPage).CopyTo(inBuffer, spsdOffset + 4);
            BitConverter.GetBytes(NVMe.LogPageHealthInfo).CopyTo(inBuffer, spsdOffset + 8);
            BitConverter.GetBytes(0).CopyTo(inBuffer, spsdOffset + 12);
            BitConverter.GetBytes(protocolDataOffset).CopyTo(inBuffer, spsdOffset + 16);
            BitConverter.GetBytes(protocolDataLength).CopyTo(inBuffer, spsdOffset + 20);

            int outSize = 8 + protocolDataOffset + protocolDataLength;
            var outBuffer = new byte[outSize];

            bool ok = DeviceIoControl(handle, Ioctl.StorageQueryProperty,
                inBuffer, inBuffer.Length, outBuffer, outSize, out int bytesReturned, IntPtr.Zero);

            if (!ok || bytesReturned < outSize) return null;

            return outBuffer;
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetNvmeHealthLog failed for disk {deviceNumber}: {ex.Message}");
            return null;
        }
    }

    // ── Dump ──

    public Dictionary<string, object?> GetFullDump(int deviceNumber)
    {
        var dump = new Dictionary<string, object?>();

        // Win32_DiskDrive
        try
        {
            using var s = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_DiskDrive WHERE Index = {deviceNumber}");
            foreach (var disk in s.Get())
                dump["Win32_DiskDrive"] = ToPropertyBag(disk);
        }
        catch (Exception ex) { dump["Win32_DiskDrive_Error"] = ex.Message; }

        // MSFT_PhysicalDisk
        try
        {
            var scope = CreateStorageScope();
            using var s = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceNumber}'"));
            foreach (var disk in s.Get())
                dump["MSFT_PhysicalDisk"] = ToPropertyBag(disk);
        }
        catch (Exception ex) { dump["MSFT_PhysicalDisk_Error"] = ex.Message; }

        // MSFT_Disk
        try
        {
            var scope = CreateStorageScope();
            using var s = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {deviceNumber}"));
            foreach (var disk in s.Get())
                dump["MSFT_Disk"] = ToPropertyBag(disk);
        }
        catch (Exception ex) { dump["MSFT_Disk_Error"] = ex.Message; }

        // MSFT_StorageReliabilityCounter
        try
        {
            var scope = CreateStorageScope();
            using var ds = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceNumber}'"));
            foreach (ManagementObject disk in ds.Get())
            {
                var related = disk.GetRelated("MSFT_StorageReliabilityCounter");
                foreach (var counter in related)
                    dump["MSFT_StorageReliabilityCounter"] = ToPropertyBag(counter);
            }
        }
        catch (Exception ex) { dump["MSFT_StorageReliabilityCounter_Error"] = ex.Message; }

        // MSFT_Partitions
        try
        {
            var scope = CreateStorageScope();
            using var ds = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {deviceNumber}"));
            var partitions = new List<Dictionary<string, object?>>();
            foreach (ManagementObject msftDisk in ds.Get())
            {
                var related = msftDisk.GetRelated("MSFT_Partition");
                foreach (var part in related)
                    partitions.Add(ToPropertyBag(part));
            }
            if (partitions.Count > 0)
                dump["MSFT_Partitions"] = partitions;
        }
        catch (Exception ex) { dump["MSFT_Partitions_Error"] = ex.Message; }

        // Drive letters
        try
        {
            var (partitions, letters) = GetDriveLettersMsft(deviceNumber);
            dump["DriveLetters"] = new Dictionary<string, object?>
            {
                ["Partitions"] = partitions,
                ["Letters"] = letters
            };
        }
        catch (Exception ex) { dump["DriveLetters_Error"] = ex.Message; }

        // SMART data (raw bytes, base64)
        try
        {
            var (attrs, thresholds) = GetSmartData(deviceNumber);
            if (attrs != null) dump["SMART_Attributes_Raw"] = Convert.ToBase64String(attrs);
            if (thresholds != null) dump["SMART_Thresholds_Raw"] = Convert.ToBase64String(thresholds);
        }
        catch (Exception ex) { dump["SMART_Error"] = ex.Message; }

        // NVMe health log (raw bytes, base64)
        try
        {
            var nvme = GetNvmeHealthLog(deviceNumber);
            if (nvme != null) dump["NVMe_HealthLog_Raw"] = Convert.ToBase64String(nvme);
        }
        catch (Exception ex) { dump["NVMe_Error"] = ex.Message; }

        // ATA IDENTIFY (raw bytes, base64) — try SAT first, then SMART
        try
        {
            var ata = GetAtaIdentifyViaSat(deviceNumber);
            ata ??= GetAtaIdentifyViaSmart(deviceNumber);
            if (ata != null) dump["ATA_Identify_Raw"] = Convert.ToBase64String(ata);
        }
        catch (Exception ex) { dump["ATA_Identify_Error"] = ex.Message; }

        // USB bridge VID/PID
        try
        {
            if (dump.TryGetValue("Win32_DiskDrive", out var w32) && w32 is Dictionary<string, object?> w32Dict
                && w32Dict.TryGetValue("PNPDeviceID", out var pnpVal))
            {
                var pnpId = pnpVal?.ToString() ?? "";
                var bridge = GetUsbBridgeVidPid(pnpId);
                if (bridge.HasValue)
                {
                    dump["USB_Bridge_VID"] = bridge.Value.Vid;
                    dump["USB_Bridge_PID"] = bridge.Value.Pid;
                }
            }
        }
        catch (Exception ex) { dump["USB_Bridge_Error"] = ex.Message; }

        // Metadata
        dump["DumpVersion"] = (long)2;
        dump["DumpTimestamp"] = DateTime.UtcNow.ToString("o");

        return dump;
    }
}
