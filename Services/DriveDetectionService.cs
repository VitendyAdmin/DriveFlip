using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DriveFlip.Models;
using Microsoft.Win32.SafeHandles;

namespace DriveFlip.Services;

[SupportedOSPlatform("windows")]
public static class DriveDetectionService
{
    private static readonly Dictionary<int, string> BusTypeMap = new()
    {
        [1] = "SCSI", [2] = "ATAPI", [3] = "ATA", [5] = "1394", [6] = "SSA",
        [7] = "USB", [8] = "RAID", [9] = "iSCSI", [10] = "SAS", [11] = "SATA",
        [12] = "SD", [13] = "MMC", [15] = "File Backed Virtual",
        [16] = "Storage Spaces", [17] = "NVMe"
    };

    public static List<PhysicalDrive> DetectDrives()
    {
        var drives = new List<PhysicalDrive>();
        Logger.Info("Starting drive detection via WMI...");

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_DiskDrive");

            foreach (var disk in searcher.Get())
            {
                var drive = new PhysicalDrive
                {
                    DeviceNumber = Convert.ToInt32(disk["Index"]),
                    Model = disk["Model"]?.ToString()?.Trim() ?? "Unknown",
                    SerialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "",
                    InterfaceType = disk["InterfaceType"]?.ToString() ?? "Unknown",
                    MediaType = disk["MediaType"]?.ToString() ?? "Unknown",
                    SizeBytes = Convert.ToInt64(disk["Size"] ?? 0),
                    BytesPerSector = Convert.ToInt32(disk["BytesPerSector"] ?? 512),
                    FirmwareVersion = disk["FirmwareRevision"]?.ToString()?.Trim() ?? "",
                    Status = disk["Status"]?.ToString() ?? "Unknown"
                };

                // Preliminary removable flag from Win32_DiskDrive
                drive.IsRemovable = drive.MediaType.Contains("Removable", StringComparison.OrdinalIgnoreCase)
                    || drive.InterfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase);

                // Enrich with MSFT_PhysicalDisk: accurate BusType, IsRemovable, InterfaceType
                EnrichFromStorageApi(drive);

                // Get partitions and drive letters (Win32 chain, then MSFT fallback)
                PopulateDriveLetters(drive);

                // Determine if this is the system drive
                var systemDriveLetter = Path.GetPathRoot(Environment.SystemDirectory)?
                    .TrimEnd('\\');
                drive.IsSystemDrive = drive.DriveLetters
                    .Any(dl => dl.Equals(systemDriveLetter, StringComparison.OrdinalIgnoreCase));

                drives.Add(drive);
                Logger.Info($"Detected: Disk {drive.DeviceNumber} — {drive.Model} ({drive.DisplaySize}) [{drive.InterfaceType}] Letters=[{drive.DriveLettersSummary}] System={drive.IsSystemDrive} Removable={drive.IsRemovable}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Drive detection failed", ex);
        }

        Logger.Info($"Drive detection complete: {drives.Count} drive(s) found.");
        return drives.OrderBy(d => d.DeviceNumber).ToList();
    }

    public static DriveHealthInfo QueryHealthInfo(int deviceNumber)
    {
        var info = new DriveHealthInfo();

        try
        {
            // Query MSFT_PhysicalDisk for health, media type, bus type, spindle speed
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            var query = new ObjectQuery(
                $"SELECT HealthStatus, MediaType, BusType, SpindleSpeed FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceNumber}'");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (var disk in searcher.Get())
            {
                var healthVal = Convert.ToInt32(disk["HealthStatus"] ?? 0);
                info.HealthStatus = healthVal switch
                {
                    0 => "Healthy",
                    1 => "Warning",
                    2 => "Unhealthy",
                    _ => $"Unknown ({healthVal})"
                };

                var mediaVal = Convert.ToInt32(disk["MediaType"] ?? 0);
                info.MediaType = mediaVal switch
                {
                    3 => "HDD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => "Unspecified"
                };

                var busVal = Convert.ToInt32(disk["BusType"] ?? 0);
                info.BusType = BusTypeMap.TryGetValue(busVal, out var name) ? name : $"Other ({busVal})";

                var spindle = disk["SpindleSpeed"];
                if (spindle != null)
                    info.SpindleSpeed = Convert.ToInt32(spindle);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"MSFT_PhysicalDisk query failed for disk {deviceNumber}: {ex.Message}");
        }

        try
        {
            // Query MSFT_StorageReliabilityCounter for SMART-like data
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            // Get the MSFT_PhysicalDisk instance path first, then its reliability counters
            var diskQuery = new ObjectQuery(
                $"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceNumber}'");
            using var diskSearcher = new ManagementObjectSearcher(scope, diskQuery);

            foreach (ManagementObject disk in diskSearcher.Get())
            {
                try
                {
                    var related = disk.GetRelated("MSFT_StorageReliabilityCounter");
                    foreach (var counter in related)
                    {
                        var temp = counter["Temperature"];
                        if (temp != null) info.Temperature = Convert.ToInt32(temp);

                        var poh = counter["PowerOnHours"];
                        if (poh != null) info.PowerOnHours = Convert.ToInt64(poh);

                        var re = counter["ReadErrorsTotal"];
                        if (re != null) info.ReadErrors = Convert.ToInt64(re);

                        var we = counter["WriteErrorsTotal"];
                        if (we != null) info.WriteErrors = Convert.ToInt64(we);

                        var wear = counter["Wear"];
                        if (wear != null) info.Wear = Convert.ToInt32(wear);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Reliability counter query failed for disk {deviceNumber}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"MSFT_StorageReliabilityCounter query failed for disk {deviceNumber}: {ex.Message}");
        }

        return info;
    }

    /// <summary>
    /// Uses MSFT_PhysicalDisk to get the real BusType, override the misleading
    /// Win32_DiskDrive.InterfaceType (which reports "SCSI" for NVMe), and refine IsRemovable.
    /// </summary>
    private static void EnrichFromStorageApi(PhysicalDrive drive)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            var query = new ObjectQuery(
                $"SELECT BusType FROM MSFT_PhysicalDisk WHERE DeviceId = '{drive.DeviceNumber}'");
            using var searcher = new ManagementObjectSearcher(scope, query);

            foreach (var disk in searcher.Get())
            {
                var busType = Convert.ToInt32(disk["BusType"] ?? 0);

                // Override InterfaceType with accurate bus type
                if (BusTypeMap.TryGetValue(busType, out var busName))
                    drive.InterfaceType = busName;

                // BusType 7 = USB, 12 = SD, 13 = MMC
                if (busType is 7 or 12 or 13)
                    drive.IsRemovable = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"MSFT_PhysicalDisk BusType query failed for disk {drive.DeviceNumber}: {ex.Message}");
        }
    }

    private static void PopulateDriveLetters(PhysicalDrive drive)
    {
        try
        {
            // Query partition-to-logical-disk mapping via WMI
            using var partSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{drive.DevicePath.Replace("\\", "\\\\")}'}} " +
                "WHERE AssocClass=Win32_DiskDriveToDiskPartition");

            foreach (var partition in partSearcher.Get())
            {
                var partId = partition["DeviceID"]?.ToString() ?? "";
                drive.Partitions.Add(partId);

                using var logicalSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} " +
                    "WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (var logical in logicalSearcher.Get())
                {
                    var letter = logical["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(letter))
                        drive.DriveLetters.Add(letter);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Could not get drive letters for disk {drive.DeviceNumber} (Win32 chain): {ex.Message}");
        }

        // Fallback: if Win32 chain found no letters, try MSFT_Disk → MSFT_Partition → volumes
        if (drive.DriveLetters.Count == 0)
            PopulateDriveLettersMsft(drive);
    }

    // ── SMART Data Query ──

    private static readonly Dictionary<byte, (string Name, bool IsPreFail)> SmartAttributeNames = new()
    {
        [1]   = ("Read Error Rate", true),
        [3]   = ("Spin-Up Time", true),
        [4]   = ("Start/Stop Count", false),
        [5]   = ("Reallocated Sector Count", true),
        [7]   = ("Seek Error Rate", true),
        [9]   = ("Power-On Hours", false),
        [10]  = ("Spin Retry Count", true),
        [12]  = ("Power Cycle Count", false),
        [187] = ("Reported Uncorrectable Errors", true),
        [188] = ("Command Timeout", false),
        [190] = ("Airflow Temperature", false),
        [194] = ("Temperature", false),
        [196] = ("Reallocated Event Count", false),
        [197] = ("Current Pending Sector Count", true),
        [198] = ("Offline Uncorrectable", true),
        [199] = ("UltraDMA CRC Error Count", false),
        [200] = ("Multi-Zone Error Rate", true),
        [201] = ("Soft Read Error Rate", true),
        [240] = ("Head Flying Hours", false),
        [241] = ("Total LBAs Written", false),
        [242] = ("Total LBAs Read", false),
    };

    public static void QueryDetailedSmartData(int deviceNumber, DriveHealthInfo info, string busType)
    {
        QueryExtendedReliability(deviceNumber, info);

        bool isSata = busType.Equals("SATA", StringComparison.OrdinalIgnoreCase)
                   || busType.Equals("ATA", StringComparison.OrdinalIgnoreCase)
                   || busType.Equals("ATAPI", StringComparison.OrdinalIgnoreCase);

        if (isSata)
            QueryRawSmartAttributes(deviceNumber, info);

        info.SmartDataQueried = true;
        ComputeRiskLevel(info);
    }

    private static void QueryExtendedReliability(int deviceNumber, DriveHealthInfo info)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            var diskQuery = new ObjectQuery(
                $"SELECT * FROM MSFT_PhysicalDisk WHERE DeviceId = '{deviceNumber}'");
            using var diskSearcher = new ManagementObjectSearcher(scope, diskQuery);

            foreach (ManagementObject disk in diskSearcher.Get())
            {
                try
                {
                    var related = disk.GetRelated("MSFT_StorageReliabilityCounter");
                    foreach (var counter in related)
                    {
                        var v = counter["ReadErrorsCorrected"];
                        if (v != null) info.ReadErrorsCorrected = Convert.ToInt64(v);

                        v = counter["ReadErrorsUncorrected"];
                        if (v != null) info.ReadErrorsUncorrected = Convert.ToInt64(v);

                        v = counter["WriteErrorsCorrected"];
                        if (v != null) info.WriteErrorsCorrected = Convert.ToInt64(v);

                        v = counter["WriteErrorsUncorrected"];
                        if (v != null) info.WriteErrorsUncorrected = Convert.ToInt64(v);

                        v = counter["ReadLatencyMax"];
                        if (v != null) info.ReadLatencyMax = Convert.ToInt64(v);

                        v = counter["WriteLatencyMax"];
                        if (v != null) info.WriteLatencyMax = Convert.ToInt64(v);

                        v = counter["FlushLatencyMax"];
                        if (v != null) info.FlushLatencyMax = Convert.ToInt64(v);

                        v = counter["StartStopCycleCount"];
                        if (v != null) info.StartStopCycleCount = Convert.ToInt64(v);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Extended reliability query failed for disk {deviceNumber}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Extended reliability scope failed for disk {deviceNumber}: {ex.Message}");
        }
    }

    // ── P/Invoke for ATA SMART ──

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

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint SMART_RCV_DRIVE_DATA = 0x0007C088;

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

    private static void QueryRawSmartAttributes(int deviceNumber, DriveHealthInfo info)
    {
        try
        {
            using var handle = CreateFile(
                $"\\\\.\\PhysicalDrive{deviceNumber}",
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle.IsInvalid)
            {
                Logger.Warning($"Could not open PhysicalDrive{deviceNumber} for SMART query: access denied or invalid handle.");
                return;
            }

            var cmdIn = new SENDCMDINPARAMS
            {
                cBufferSize = 512,
                irDriveRegs = new IDEREGS
                {
                    bFeaturesReg = 0xD0,      // SMART READ DATA
                    bSectorCountReg = 1,
                    bSectorNumberReg = 1,
                    bCylLowReg = 0x4F,
                    bCylHighReg = 0xC2,
                    bDriveHeadReg = (byte)(0xA0 | ((deviceNumber & 1) << 4)),
                    bCommandReg = 0xB0         // SMART command
                },
                bDriveNumber = (byte)deviceNumber,
                bReserved = new byte[3],
                dwReserved = new byte[4],
                bBuffer = new byte[1]
            };

            int inSize = Marshal.SizeOf<SENDCMDINPARAMS>();
            // Output: SENDCMDOUTPARAMS header (16 bytes) + 512-byte data buffer
            int outSize = 16 + 512;
            var outBuffer = new byte[outSize];

            bool ok = DeviceIoControl(handle, SMART_RCV_DRIVE_DATA,
                ref cmdIn, inSize, outBuffer, outSize, out _, IntPtr.Zero);

            if (!ok)
            {
                Logger.Warning($"SMART DeviceIoControl failed for disk {deviceNumber}. Error: {Marshal.GetLastWin32Error()}");
                return;
            }

            info.SmartSupported = true;

            // Parse attribute table: starts at offset 18 (16-byte header + 2 bytes SMART version)
            // 30 slots × 12 bytes each
            int tableOffset = 18;
            for (int i = 0; i < 30; i++)
            {
                int offset = tableOffset + i * 12;
                if (offset + 12 > outBuffer.Length) break;

                byte attrId = outBuffer[offset];
                if (attrId == 0) continue;

                // Bytes: [0]=ID, [1-2]=flags, [3]=current, [4]=worst, [5-10]=raw (6 bytes LE)
                ushort flags = BitConverter.ToUInt16(outBuffer, offset + 1);
                byte current = outBuffer[offset + 3];
                byte worst = outBuffer[offset + 4];
                long raw = BitConverter.ToInt32(outBuffer, offset + 5); // lower 4 bytes of raw
                if (raw < 0) raw = BitConverter.ToUInt32(outBuffer, offset + 5);

                bool isPreFail = (flags & 0x01) != 0;
                string name = SmartAttributeNames.TryGetValue(attrId, out var entry) ? entry.Name : $"Attribute {attrId}";
                if (SmartAttributeNames.ContainsKey(attrId))
                    isPreFail = SmartAttributeNames[attrId].IsPreFail;

                info.SmartAttributes.Add(new SmartAttribute(attrId, name, current, worst, raw, isPreFail));
            }

            Logger.Info($"SMART: Parsed {info.SmartAttributes.Count} attributes for disk {deviceNumber}.");
        }
        catch (Exception ex)
        {
            Logger.Warning($"SMART attribute query failed for disk {deviceNumber}: {ex.Message}");
        }
    }

    private static long GetSmartRaw(DriveHealthInfo info, byte id)
    {
        var attr = info.SmartAttributes.FirstOrDefault(a => a.Id == id);
        return attr?.RawValue ?? 0;
    }

    private static void ComputeRiskLevel(DriveHealthInfo info)
    {
        var reasons = new List<string>();

        long pending = GetSmartRaw(info, 197);
        long offlineUncorrectable = GetSmartRaw(info, 198);
        long reportedUncorrectable = GetSmartRaw(info, 187);
        long reallocated = GetSmartRaw(info, 5);
        long spinRetry = GetSmartRaw(info, 10);
        long crcErrors = GetSmartRaw(info, 199);
        int wear = info.Wear ?? 0;
        int temp = info.Temperature ?? 0;
        long uncorrectedReads = info.ReadErrorsUncorrected ?? 0;
        long uncorrectedWrites = info.WriteErrorsUncorrected ?? 0;

        bool critical = false;
        bool warning = false;

        // Critical conditions
        if (pending > 0) { critical = true; reasons.Add($"Pending sectors: {pending}"); }
        if (offlineUncorrectable > 0) { critical = true; reasons.Add($"Offline uncorrectable: {offlineUncorrectable}"); }
        if (reportedUncorrectable > 0) { critical = true; reasons.Add($"Uncorrectable errors: {reportedUncorrectable}"); }
        if (uncorrectedReads > 0) { critical = true; reasons.Add($"Uncorrected read errors: {uncorrectedReads}"); }
        if (uncorrectedWrites > 0) { critical = true; reasons.Add($"Uncorrected write errors: {uncorrectedWrites}"); }
        if (reallocated > 100) { critical = true; reasons.Add($"Reallocated sectors: {reallocated}"); }
        if (wear > 90) { critical = true; reasons.Add($"SSD wear: {wear}%"); }
        if (temp > 65) { critical = true; reasons.Add($"Temperature: {temp} °C"); }

        // Warning conditions (only if not already critical for same reason)
        if (!critical)
        {
            if (reallocated > 0) { warning = true; reasons.Add($"Reallocated sectors: {reallocated}"); }
            if (spinRetry > 0) { warning = true; reasons.Add($"Spin retries: {spinRetry}"); }
            if (crcErrors > 100) { warning = true; reasons.Add($"CRC errors: {crcErrors}"); }
            if (wear > 70) { warning = true; reasons.Add($"SSD wear: {wear}%"); }
            if (temp > 55) { warning = true; reasons.Add($"Temperature: {temp} °C"); }
        }

        if (critical)
        {
            info.RiskLevel = DriveRiskLevel.Critical;
            info.RiskSummary = string.Join("; ", reasons);
        }
        else if (warning)
        {
            info.RiskLevel = DriveRiskLevel.Warning;
            info.RiskSummary = string.Join("; ", reasons);
        }
        else
        {
            info.RiskLevel = DriveRiskLevel.Good;
            info.RiskSummary = "No risk indicators detected.";
        }
    }

    /// <summary>
    /// Fallback volume mapping via the modern Storage namespace.
    /// MSFT_Disk → MSFT_Partition (with DriveLetter) covers NVMe and other drives
    /// where the Win32_DiskDrive association chain returns nothing.
    /// </summary>
    private static void PopulateDriveLettersMsft(PhysicalDrive drive)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            // Find the MSFT_Disk matching this device number
            var diskQuery = new ObjectQuery(
                $"SELECT * FROM MSFT_Disk WHERE Number = '{drive.DeviceNumber}'");
            using var diskSearcher = new ManagementObjectSearcher(scope, diskQuery);

            foreach (ManagementObject msftDisk in diskSearcher.Get())
            {
                // Get associated partitions
                var partitions = msftDisk.GetRelated("MSFT_Partition");
                foreach (ManagementObject partition in partitions)
                {
                    var partId = partition["PartitionNumber"]?.ToString();
                    if (!string.IsNullOrEmpty(partId) && drive.Partitions.Count == 0)
                        drive.Partitions.Add($"Disk #{drive.DeviceNumber}, Partition #{partId}");

                    // DriveLetter is a char; 0 means no letter assigned
                    var driveLetterObj = partition["DriveLetter"];
                    if (driveLetterObj != null)
                    {
                        var driveLetter = Convert.ToChar(driveLetterObj);
                        if (driveLetter != '\0')
                            drive.DriveLetters.Add($"{driveLetter}:");
                    }
                }
            }

            if (drive.DriveLetters.Count > 0)
                Logger.Info($"Disk {drive.DeviceNumber}: MSFT fallback found letters [{string.Join(", ", drive.DriveLetters)}]");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Could not get drive letters for disk {drive.DeviceNumber} (MSFT fallback): {ex.Message}");
        }
    }
}
