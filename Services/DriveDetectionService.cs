using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;
using DriveFlip.Constants;
using DriveFlip.Models;

namespace DriveFlip.Services;

[SupportedOSPlatform("windows")]
public class DriveDetectionService
{
    private readonly IDriveDataProvider _provider;

    public DriveDetectionService() : this(new WmiDriveDataProvider()) { }
    public DriveDetectionService(IDriveDataProvider provider) { _provider = provider; }

    // ── Property Bag Helpers ──

    private static string SafeString(Dictionary<string, object?> bag, string key, string defaultValue = "")
    {
        if (bag.TryGetValue(key, out var val) && val != null)
            return val.ToString()?.Trim() ?? defaultValue;
        return defaultValue;
    }

    private static int SafeInt(Dictionary<string, object?> bag, string key, int defaultValue = 0)
    {
        if (bag.TryGetValue(key, out var val) && val != null)
        {
            try { return Convert.ToInt32(val); } catch { }
        }
        return defaultValue;
    }

    private static long SafeLong(Dictionary<string, object?> bag, string key, long defaultValue = 0)
    {
        if (bag.TryGetValue(key, out var val) && val != null)
        {
            try { return Convert.ToInt64(val); } catch { }
        }
        return defaultValue;
    }

    private static int? SafeNullableInt(Dictionary<string, object?> bag, string key)
    {
        if (bag.TryGetValue(key, out var val) && val != null)
        {
            try { return Convert.ToInt32(val); } catch { }
        }
        return null;
    }

    private static long? SafeNullableLong(Dictionary<string, object?> bag, string key)
    {
        if (bag.TryGetValue(key, out var val) && val != null)
        {
            try { return Convert.ToInt64(val); } catch { }
        }
        return null;
    }

    private static bool? SafeBool(Dictionary<string, object?> bag, string key)
    {
        if (bag.TryGetValue(key, out var val) && val != null)
        {
            if (val is bool b) return b;
            try { return Convert.ToBoolean(val); } catch { }
        }
        return null;
    }

    // ── USB Bridge Chip Lookup ──

    private static readonly Dictionary<string, string> UsbBridgeChips = new(StringComparer.OrdinalIgnoreCase)
    {
        // ASMedia
        ["174C:55AA"] = "ASMedia ASM1153e",
        ["174C:225A"] = "ASMedia ASM225CM",
        ["174C:235C"] = "ASMedia ASM235CM",
        ["174C:2362"] = "ASMedia ASM2362",
        ["174C:5106"] = "ASMedia ASM1051",
        // JMicron
        ["152D:0578"] = "JMicron JMS578",
        ["152D:0583"] = "JMicron JMS583",
        ["152D:1561"] = "JMicron JMS561",
        ["152D:0567"] = "JMicron JMS567",
        // VIA Labs
        ["2109:0715"] = "VIA Labs VL715",
        ["2109:0716"] = "VIA Labs VL716",
        // Realtek
        ["0BDA:9210"] = "Realtek RTL9210",
        ["0BDA:9211"] = "Realtek RTL9210B",
        // Genesys Logic
        ["05E3:0749"] = "Genesys Logic GL3349",
        ["05E3:0746"] = "Genesys Logic GL3346",
        // Cypress
        ["04B4:6830"] = "Cypress CY7C68300",
        ["04B4:6831"] = "Cypress CY7C68310",
    };

    // ── SMART Attribute Names ──

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
        // SSD-specific
        [170] = ("Available Reserved Space", false),
        [171] = ("SSD Program Fail Count", true),
        [172] = ("SSD Erase Fail Count", true),
        [173] = ("SSD Wear Leveling Count", false),
        [174] = ("Unexpected Power Loss", false),
        [175] = ("Power Loss Protection Failure", true),
        [176] = ("Erase Fail Count (chip)", true),
        [177] = ("Wear Range Delta", false),
        // Additional
        [183] = ("SATA Downshift Error Count", false),
        [184] = ("End-to-End Error", true),
        [187] = ("Reported Uncorrectable Errors", true),
        [188] = ("Command Timeout", false),
        [189] = ("High Fly Writes", false),
        [190] = ("Airflow Temperature", false),
        [191] = ("G-Sense Error Rate", false),
        [192] = ("Power-Off Retract Count", false),
        [193] = ("Load/Unload Cycle Count", false),
        [194] = ("Temperature", false),
        [195] = ("Hardware ECC Recovered", false),
        [196] = ("Reallocated Event Count", false),
        [197] = ("Current Pending Sector Count", true),
        [198] = ("Offline Uncorrectable", true),
        [199] = ("UltraDMA CRC Error Count", false),
        [200] = ("Multi-Zone Error Rate", true),
        [201] = ("Soft Read Error Rate", true),
        // SSD endurance
        [230] = ("Drive Life Protection", false),
        [231] = ("SSD Life Left", true),
        [232] = ("Endurance Remaining", false),
        [233] = ("Media Wearout Indicator", false),
        [234] = ("Total LBAs Written (NAND)", false),
        [235] = ("Good Block Count", false),
        [240] = ("Head Flying Hours", false),
        [241] = ("Total LBAs Written", false),
        [242] = ("Total LBAs Read", false),
    };

    // ── Drive Detection ──

    public List<PhysicalDrive> DetectDrives()
    {
        var drives = new List<PhysicalDrive>();
        Logger.Info("Starting drive detection via WMI...");

        try
        {
            var diskBags = _provider.GetWin32DiskDrives();

            foreach (var disk in diskBags)
            {
                var drive = new PhysicalDrive
                {
                    DeviceNumber = SafeInt(disk, Wmi.Win32DiskDrive.Index),
                    Model = SafeString(disk, Wmi.Win32DiskDrive.Model, "Unknown"),
                    SerialNumber = SafeString(disk, Wmi.Win32DiskDrive.SerialNumber),
                    InterfaceType = SafeString(disk, Wmi.Win32DiskDrive.InterfaceType, "Unknown"),
                    MediaType = SafeString(disk, Wmi.Win32DiskDrive.MediaType, "Unknown"),
                    SizeBytes = SafeLong(disk, Wmi.Win32DiskDrive.Size),
                    BytesPerSector = SafeInt(disk, Wmi.Win32DiskDrive.BytesPerSector, 512),
                    FirmwareVersion = SafeString(disk, Wmi.Win32DiskDrive.FirmwareRevision),
                    Status = SafeString(disk, Wmi.Win32DiskDrive.Status, "Unknown"),
                    PnpDeviceId = SafeString(disk, Wmi.Win32DiskDrive.PNPDeviceID)
                };

                // Preliminary removable flag from Win32_DiskDrive
                drive.IsRemovable = drive.MediaType.Contains("Removable", StringComparison.OrdinalIgnoreCase)
                    || drive.InterfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase);

                // Enrich with MSFT_PhysicalDisk: accurate BusType, IsRemovable, InterfaceType
                EnrichFromStorageApi(drive);

                // Get partitions and drive letters (Win32 chain, then MSFT fallback)
                PopulateDriveLetters(drive);

                // Get partition style (GPT/MBR/RAW)
                PopulatePartitionStyle(drive);

                // Determine if this is the system drive
                var systemDriveLetter = Path.GetPathRoot(Environment.SystemDirectory)?
                    .TrimEnd('\\');
                drive.IsSystemDrive = drive.DriveLetters
                    .Any(dl => dl.Equals(systemDriveLetter, StringComparison.OrdinalIgnoreCase));

                // Infer and store manufacturer; prepend to model if it's a bare part number
                var mfr = InferManufacturer(drive.Model);
                if (mfr != null)
                {
                    drive.Manufacturer = mfr;
                    if (!drive.Model.StartsWith(mfr, StringComparison.OrdinalIgnoreCase))
                        drive.Model = $"{mfr} {drive.Model}";
                }

                // ATA features and transfer mode (SATA and USB drives only)
                PopulateAtaFeatures(drive);

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

    // ── Health Info ──

    public DriveHealthInfo QueryHealthInfo(int deviceNumber)
    {
        var info = new DriveHealthInfo();

        // Parse MSFT_PhysicalDisk
        var physDisk = _provider.GetPhysicalDisk(deviceNumber);
        if (physDisk != null)
        {
            var hs = SafeNullableInt(physDisk, Wmi.PhysicalDisk.HealthStatus);
            if (hs.HasValue) info.HealthStatus = ((WmiHealthStatus)hs.Value).ToDisplayName();

            var mt = SafeNullableInt(physDisk, Wmi.PhysicalDisk.MediaType);
            if (mt.HasValue) info.MediaType = ((StorageMediaType)mt.Value).ToDisplayName();

            var bt = SafeNullableInt(physDisk, Wmi.PhysicalDisk.BusType);
            if (bt.HasValue) info.BusType = ((StorageBusType)bt.Value).ToDisplayName();

            var spindle = SafeNullableLong(physDisk, Wmi.PhysicalDisk.SpindleSpeed);
            if (spindle.HasValue) info.SpindleSpeed = (int)Math.Min(spindle.Value, int.MaxValue);

            info.IsWriteCacheEnabled = SafeBool(physDisk, Wmi.PhysicalDisk.IsWriteCacheEnabled);
            info.IsPowerProtected = SafeBool(physDisk, Wmi.PhysicalDisk.IsPowerProtected);
        }

        // Parse MSFT_StorageReliabilityCounter (basic fields)
        var counters = _provider.GetReliabilityCounters(deviceNumber);
        if (counters != null)
        {
            var temp = SafeNullableInt(counters, Wmi.StorageReliability.Temperature);
            if (temp.HasValue) info.Temperature = temp.Value;

            var poh = SafeNullableLong(counters, Wmi.StorageReliability.PowerOnHours);
            if (poh.HasValue) info.PowerOnHours = poh.Value;

            var re = SafeNullableLong(counters, Wmi.StorageReliability.ReadErrorsTotal);
            if (re.HasValue) info.ReadErrors = re.Value;

            var we = SafeNullableLong(counters, Wmi.StorageReliability.WriteErrorsTotal);
            if (we.HasValue) info.WriteErrors = we.Value;

            var wear = SafeNullableInt(counters, Wmi.StorageReliability.Wear);
            if (wear.HasValue) info.Wear = wear.Value;
        }

        return info;
    }

    // ── Enrich from MSFT_PhysicalDisk ──

    private void EnrichFromStorageApi(PhysicalDrive drive)
    {
        var disk = _provider.GetPhysicalDisk(drive.DeviceNumber);
        if (disk == null) return;

        try
        {
            var busTypeInt = SafeInt(disk, Wmi.PhysicalDisk.BusType);
            var busType = (StorageBusType)busTypeInt;

            drive.InterfaceType = busType.ToDisplayName();

            if (busType.IsRemovableBus())
                drive.IsRemovable = true;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Disk {drive.DeviceNumber}: BusType parse failed: {ex.Message}");
        }

        // MSFT_PhysicalDisk may have the real drive model behind USB bridges
        var msftModel = SafeString(disk, Wmi.PhysicalDisk.Model);
        if (!string.IsNullOrEmpty(msftModel) && msftModel != drive.Model)
        {
            Logger.Info($"Disk {drive.DeviceNumber}: MSFT model \"{msftModel}\" overrides Win32 model \"{drive.Model}\"");
            drive.Model = msftModel;
        }
        else
        {
            var friendly = SafeString(disk, Wmi.PhysicalDisk.FriendlyName);
            if (!string.IsNullOrEmpty(friendly) && friendly != drive.Model
                && !friendly.Contains("ASMT", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"Disk {drive.DeviceNumber}: MSFT FriendlyName \"{friendly}\" overrides Win32 model \"{drive.Model}\"");
                drive.Model = friendly;
            }
        }

        var pss = SafeNullableInt(disk, Wmi.PhysicalDisk.PhysicalSectorSize);
        if (pss.HasValue) drive.PhysicalSectorSize = pss.Value;

        var lss = SafeNullableInt(disk, Wmi.PhysicalDisk.LogicalSectorSize);
        if (lss.HasValue) drive.LogicalSectorSize = lss.Value;

        var pn = SafeString(disk, Wmi.PhysicalDisk.PartNumber);
        if (!string.IsNullOrEmpty(pn)) drive.PartNumber = pn;

        // For USB drives, try SAT to get the real drive identity behind the bridge
        if (drive.InterfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase))
        {
            var satBuf = _provider.GetAtaIdentifyViaSat(drive.DeviceNumber);
            if (satBuf != null)
            {
                string satModel = ParseAtaString(satBuf, Ata.WordModel * 2, 40);
                string satSerial = ParseAtaString(satBuf, Ata.WordSerial * 2, 20);
                string satFirmware = ParseAtaString(satBuf, Ata.WordFirmware * 2, 8);

                if (!string.IsNullOrWhiteSpace(satModel))
                {
                    Logger.Info($"Disk {drive.DeviceNumber}: SAT model \"{satModel}\" overrides \"{drive.Model}\"");
                    drive.Model = satModel;
                }
                if (!string.IsNullOrWhiteSpace(satSerial) && (string.IsNullOrEmpty(drive.SerialNumber)
                    || drive.SerialNumber.Length < satSerial.Length))
                {
                    Logger.Info($"Disk {drive.DeviceNumber}: SAT serial \"{satSerial}\" overrides \"{drive.SerialNumber}\"");
                    drive.SerialNumber = satSerial;
                }
                if (!string.IsNullOrWhiteSpace(satFirmware))
                {
                    Logger.Info($"Disk {drive.DeviceNumber}: SAT firmware \"{satFirmware}\" overrides \"{drive.FirmwareVersion}\"");
                    drive.FirmwareVersion = satFirmware;
                }
            }

            // Identify USB bridge chip
            var bridge = _provider.GetUsbBridgeVidPid(drive.PnpDeviceId);
            if (bridge.HasValue)
            {
                var key = $"{bridge.Value.Vid}:{bridge.Value.Pid}";
                var chipName = UsbBridgeChips.TryGetValue(key, out var name) ? name : $"Unknown ({key})";
                drive.UsbBridgeChip = chipName;
                Logger.Info($"Disk {drive.DeviceNumber}: USB bridge identified as \"{chipName}\"");
            }
        }
    }

    // ── Drive Letters ──

    private void PopulateDriveLetters(PhysicalDrive drive)
    {
        var (partitions, letters) = _provider.GetDriveLettersWin32(drive.DevicePath);
        drive.Partitions.AddRange(partitions);
        drive.DriveLetters.AddRange(letters);

        // Fallback: if Win32 chain found no letters, try MSFT_Disk → MSFT_Partition
        if (drive.DriveLetters.Count == 0)
        {
            var (msftParts, msftLetters) = _provider.GetDriveLettersMsft(drive.DeviceNumber);
            if (drive.Partitions.Count == 0)
                drive.Partitions.AddRange(msftParts);
            drive.DriveLetters.AddRange(msftLetters);

            if (drive.DriveLetters.Count > 0)
                Logger.Info($"Disk {drive.DeviceNumber}: MSFT fallback found letters [{string.Join(", ", drive.DriveLetters)}]");
        }
    }

    // ── Partition Style ──

    private void PopulatePartitionStyle(PhysicalDrive drive)
    {
        var style = _provider.GetPartitionStyle(drive.DeviceNumber);
        if (style.HasValue)
            drive.PartitionStyle = ((DiskPartitionStyle)style.Value).ToDisplayName();
    }

    // ── SMART Data ──

    public void QueryDetailedSmartData(int deviceNumber, DriveHealthInfo info, StorageBusType busType)
    {
        QueryExtendedReliability(deviceNumber, info);

        if (busType.IsSataFamily())
            QueryRawSmartAttributes(deviceNumber, info);
        else if (busType == StorageBusType.NVMe)
            QueryNvmeHealthLog(deviceNumber, info);

        info.SmartDataQueried = true;
        ComputeRiskLevel(info);
    }

    private void QueryExtendedReliability(int deviceNumber, DriveHealthInfo info)
    {
        var counters = _provider.GetReliabilityCounters(deviceNumber);
        if (counters == null) return;

        info.ReadErrorsCorrected = SafeNullableLong(counters, Wmi.StorageReliability.ReadErrorsCorrected) ?? info.ReadErrorsCorrected;
        info.ReadErrorsUncorrected = SafeNullableLong(counters, Wmi.StorageReliability.ReadErrorsUncorrected) ?? info.ReadErrorsUncorrected;
        info.WriteErrorsCorrected = SafeNullableLong(counters, Wmi.StorageReliability.WriteErrorsCorrected) ?? info.WriteErrorsCorrected;
        info.WriteErrorsUncorrected = SafeNullableLong(counters, Wmi.StorageReliability.WriteErrorsUncorrected) ?? info.WriteErrorsUncorrected;
        info.ReadLatencyMax = SafeNullableLong(counters, Wmi.StorageReliability.ReadLatencyMax) ?? info.ReadLatencyMax;
        info.WriteLatencyMax = SafeNullableLong(counters, Wmi.StorageReliability.WriteLatencyMax) ?? info.WriteLatencyMax;
        info.FlushLatencyMax = SafeNullableLong(counters, Wmi.StorageReliability.FlushLatencyMax) ?? info.FlushLatencyMax;
        info.StartStopCycleCount = SafeNullableLong(counters, Wmi.StorageReliability.StartStopCycleCount) ?? info.StartStopCycleCount;
        info.StartStopCycleCountMax = SafeNullableLong(counters, Wmi.StorageReliability.StartStopCycleCountMax) ?? info.StartStopCycleCountMax;
        info.LoadUnloadCycleCount = SafeNullableLong(counters, Wmi.StorageReliability.LoadUnloadCycleCount) ?? info.LoadUnloadCycleCount;
        info.LoadUnloadCycleCountMax = SafeNullableLong(counters, Wmi.StorageReliability.LoadUnloadCycleCountMax) ?? info.LoadUnloadCycleCountMax;

        var tempMax = SafeNullableInt(counters, Wmi.StorageReliability.TemperatureMax);
        if (tempMax.HasValue) info.TemperatureMax = tempMax.Value;

        var mfgDate = SafeString(counters, Wmi.StorageReliability.ManufactureDate);
        if (!string.IsNullOrEmpty(mfgDate)) info.ManufactureDate = mfgDate;
    }

    private void QueryRawSmartAttributes(int deviceNumber, DriveHealthInfo info)
    {
        var (attrBuffer, threshBuffer) = _provider.GetSmartData(deviceNumber);
        if (attrBuffer == null) return;

        info.SmartSupported = true;

        // Parse attribute table: starts at TableOffset (header + version bytes)
        for (int i = 0; i < Smart.SlotCount; i++)
        {
            int offset = Smart.TableOffset + i * Smart.SlotSize;
            if (offset + Smart.SlotSize > attrBuffer.Length) break;

            byte attrId = attrBuffer[offset];
            if (attrId == 0) continue;

            ushort flags = BitConverter.ToUInt16(attrBuffer, offset + 1);
            byte current = attrBuffer[offset + 3];
            byte worst = attrBuffer[offset + 4];
            long raw = BitConverter.ToInt32(attrBuffer, offset + 5);
            if (raw < 0) raw = BitConverter.ToUInt32(attrBuffer, offset + 5);

            bool isPreFail = (flags & 0x01) != 0;
            string name = SmartAttributeNames.TryGetValue(attrId, out var entry) ? entry.Name : $"Attribute {attrId}";
            if (SmartAttributeNames.ContainsKey(attrId))
                isPreFail = SmartAttributeNames[attrId].IsPreFail;

            info.SmartAttributes.Add(new SmartAttribute(attrId, name, current, worst, raw, isPreFail));
        }

        // Parse thresholds and merge
        if (threshBuffer != null)
        {
            var thresholds = ParseSmartThresholds(threshBuffer);
            for (int i = 0; i < info.SmartAttributes.Count; i++)
            {
                var attr = info.SmartAttributes[i];
                if (thresholds.TryGetValue(attr.Id, out var thresh))
                    info.SmartAttributes[i] = attr with { Threshold = thresh };
            }
        }

        Logger.Info($"SMART: Parsed {info.SmartAttributes.Count} attributes for disk {deviceNumber}.");
    }

    private static Dictionary<byte, byte> ParseSmartThresholds(byte[] buffer)
    {
        var thresholds = new Dictionary<byte, byte>();
        for (int i = 0; i < Smart.SlotCount; i++)
        {
            int offset = Smart.TableOffset + i * Smart.SlotSize;
            if (offset + Smart.SlotSize > buffer.Length) break;

            byte attrId = buffer[offset];
            if (attrId == 0) continue;

            byte threshold = buffer[offset + 1];
            thresholds[attrId] = threshold;
        }
        return thresholds;
    }

    // ── NVMe Health Log ──

    private void QueryNvmeHealthLog(int deviceNumber, DriveHealthInfo info)
    {
        var buffer = _provider.GetNvmeHealthLog(deviceNumber);
        if (buffer == null) return;

        int dataOffset = 8 + NVMe.ProtocolDataStructSize;
        ParseNvmeHealthLog(buffer, dataOffset, info);
        info.IsNvme = true;

        Logger.Info($"NVMe health log parsed for disk {deviceNumber}: spare={info.NvmeAvailableSpare}%, used={info.NvmePercentageUsed}%");
    }

    private static void ParseNvmeHealthLog(byte[] data, int offset, DriveHealthInfo info)
    {
        if (offset + NVMe.HealthLogSize > data.Length) return;

        int tempK = BitConverter.ToUInt16(data, offset + NVMe.OffsetCompositeTemp);
        if (tempK > 0) info.Temperature ??= tempK - NVMe.KelvinOffset;

        info.NvmeAvailableSpare = data[offset + NVMe.OffsetAvailableSpare];
        info.NvmeAvailableSpareThreshold = data[offset + NVMe.OffsetSpareThreshold];
        info.NvmePercentageUsed = data[offset + NVMe.OffsetPercentageUsed];

        info.NvmeDataUnitsRead = BitConverter.ToInt64(data, offset + NVMe.OffsetDataUnitsRead);
        info.NvmeDataUnitsWritten = BitConverter.ToInt64(data, offset + NVMe.OffsetDataUnitsWritten);

        info.NvmeControllerBusyTime = BitConverter.ToInt64(data, offset + NVMe.OffsetControllerBusy);
        info.NvmePowerCycles = BitConverter.ToInt64(data, offset + NVMe.OffsetPowerCycles);

        long nvmePoh = BitConverter.ToInt64(data, offset + NVMe.OffsetPowerOnHours);
        if (nvmePoh > 0) info.PowerOnHours ??= nvmePoh;

        info.NvmeUnsafeShutdowns = BitConverter.ToInt64(data, offset + NVMe.OffsetUnsafeShutdowns);
        info.NvmeMediaErrors = BitConverter.ToInt64(data, offset + NVMe.OffsetMediaErrors);

        ushort tempSensor1 = BitConverter.ToUInt16(data, offset + NVMe.OffsetTempSensor1);
        if (tempSensor1 > 0) info.NvmeTemperatureSensor1 = tempSensor1 - NVMe.KelvinOffset;
        ushort tempSensor2 = BitConverter.ToUInt16(data, offset + NVMe.OffsetTempSensor2);
        if (tempSensor2 > 0) info.NvmeTemperatureSensor2 = tempSensor2 - NVMe.KelvinOffset;
    }

    // ── ATA IDENTIFY DEVICE ──

    private byte[]? GetAtaIdentifyBuffer(int deviceNumber, string interfaceType)
    {
        if (interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase))
            return _provider.GetAtaIdentifyViaSat(deviceNumber);

        if (interfaceType is "SATA" or "ATA" or "ATAPI")
            return _provider.GetAtaIdentifyViaSmart(deviceNumber);

        return null;
    }

    private void PopulateAtaFeatures(PhysicalDrive drive)
    {
        var buf = GetAtaIdentifyBuffer(drive.DeviceNumber, drive.InterfaceType);
        if (buf == null || buf.Length < 512) return;

        // ATA IDENTIFY serial (byte-swapped) — always correct
        string ataSerial = ParseAtaString(buf, Ata.WordSerial * 2, 20);
        if (!string.IsNullOrWhiteSpace(ataSerial) && ataSerial != drive.SerialNumber)
        {
            Logger.Info($"Disk {drive.DeviceNumber}: ATA IDENTIFY serial \"{ataSerial}\" overrides WMI serial \"{drive.SerialNumber}\"");
            drive.SerialNumber = ataSerial;
        }

        var features = new List<string>();

        ushort word82 = BitConverter.ToUInt16(buf, Ata.WordFeatures82 * 2);
        ushort word83 = BitConverter.ToUInt16(buf, Ata.WordFeatures83 * 2);
        ushort word85 = BitConverter.ToUInt16(buf, Ata.WordFeaturesEnabled85 * 2);
        ushort word86 = BitConverter.ToUInt16(buf, Ata.WordFeaturesEnabled86 * 2);
        ushort word69 = BitConverter.ToUInt16(buf, Ata.WordTrimSupport * 2);
        ushort word75 = BitConverter.ToUInt16(buf, Ata.WordQueueDepth * 2);
        ushort word76 = BitConverter.ToUInt16(buf, Ata.WordSataCapabilities * 2);
        ushort word77 = BitConverter.ToUInt16(buf, Ata.WordSataCurrent * 2);

        if ((word82 & 0x01) != 0) features.Add((word85 & 0x01) != 0 ? "SMART" : "SMART (off)");
        if ((word82 & 0x02) != 0) features.Add((word85 & 0x02) != 0 ? "Security" : "Security (off)");
        if ((word83 & 0x08) != 0) features.Add((word86 & 0x08) != 0 ? "APM" : "APM (off)");
        if ((word82 & 0x20) != 0) features.Add((word85 & 0x20) != 0 ? "Write Cache" : "Write Cache (off)");
        if ((word82 & 0x40) != 0) features.Add((word85 & 0x40) != 0 ? "Read Look-ahead" : "Read Look-ahead (off)");
        if ((word83 & 0x400) != 0) features.Add("48-bit LBA");
        if ((word76 & 0x100) != 0)
        {
            int depth = (word75 & 0x1F) + 1;
            features.Add($"NCQ (depth {depth})");
        }
        if ((word69 & 0x4000) != 0) features.Add("TRIM");

        drive.SupportedFeatures = string.Join(", ", features);

        // Transfer mode — SATA speed from word 76/77
        if ((word76 & 0x0E) != 0)
        {
            string maxSpeed = (word76 & 0x08) != 0 ? "6.0 Gbps" :
                              (word76 & 0x04) != 0 ? "3.0 Gbps" :
                              (word76 & 0x02) != 0 ? "1.5 Gbps" : "?";

            int neg = (word77 >> 1) & 0x07;
            string negSpeed = neg switch { 1 => "1.5 Gbps", 2 => "3.0 Gbps", 3 => "6.0 Gbps", _ => maxSpeed };

            drive.NegotiatedSpeed = $"SATA {negSpeed}" + (negSpeed != maxSpeed ? $" (max {maxSpeed})" : "");
        }
        else
        {
            ushort word88 = BitConverter.ToUInt16(buf, Ata.WordUdma * 2);
            int selected = (word88 >> 8) & 0x7F;
            if (selected != 0)
            {
                int mode = 0;
                for (int i = 6; i >= 0; i--)
                    if ((selected & (1 << i)) != 0) { mode = i; break; }
                string[] speeds = ["16.7", "25.0", "33.3", "44.4", "66.7", "100", "133"];
                drive.NegotiatedSpeed = $"UDMA {mode} ({speeds[mode]} MB/s)";
            }
        }

        Logger.Info($"Disk {drive.DeviceNumber} ATA features: [{drive.SupportedFeatures}] Speed: {drive.NegotiatedSpeed}");
    }

    private static string ParseAtaString(byte[] data, int offset, int length)
    {
        if (offset + length > data.Length) return "";

        var chars = new char[length];
        for (int i = 0; i < length - 1; i += 2)
        {
            chars[i] = (char)data[offset + i + 1];
            chars[i + 1] = (char)data[offset + i];
        }
        return new string(chars).Trim();
    }

    // ── Risk Assessment ──

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
        if (reallocated > RiskThresholds.ReallocatedCritical) { critical = true; reasons.Add($"Reallocated sectors: {reallocated}"); }
        if (wear > RiskThresholds.WearCritical) { critical = true; reasons.Add($"SSD wear: {wear}%"); }
        if (temp > RiskThresholds.TempCritical) { critical = true; reasons.Add($"Temperature: {temp} °C"); }

        // NVMe-specific critical conditions
        if (info.IsNvme)
        {
            if (info.NvmeAvailableSpare.HasValue && info.NvmeAvailableSpareThreshold.HasValue
                && info.NvmeAvailableSpare.Value <= info.NvmeAvailableSpareThreshold.Value)
            {
                critical = true;
                reasons.Add($"NVMe spare below threshold: {info.NvmeAvailableSpare}% (threshold {info.NvmeAvailableSpareThreshold}%)");
            }
            if (info.NvmePercentageUsed.HasValue && info.NvmePercentageUsed.Value > RiskThresholds.NvmeUsedCritical)
            {
                critical = true;
                reasons.Add($"NVMe percentage used: {info.NvmePercentageUsed}%");
            }
            if (info.NvmeMediaErrors > 0)
            {
                critical = true;
                reasons.Add($"NVMe media errors: {info.NvmeMediaErrors}");
            }
        }

        // Warning conditions (only if not already critical for same reason)
        if (!critical)
        {
            if (reallocated > 0) { warning = true; reasons.Add($"Reallocated sectors: {reallocated}"); }
            if (spinRetry > 0) { warning = true; reasons.Add($"Spin retries: {spinRetry}"); }
            if (crcErrors > RiskThresholds.CrcErrorWarning) { warning = true; reasons.Add($"CRC errors: {crcErrors}"); }
            if (wear > RiskThresholds.WearWarning) { warning = true; reasons.Add($"SSD wear: {wear}%"); }
            if (temp > RiskThresholds.TempWarning) { warning = true; reasons.Add($"Temperature: {temp} °C"); }
            if (info.IsNvme && info.NvmePercentageUsed.HasValue && info.NvmePercentageUsed.Value > RiskThresholds.NvmeUsedWarning)
            {
                warning = true;
                reasons.Add($"NVMe percentage used: {info.NvmePercentageUsed}%");
            }
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

    // ── Dump ──

    public string DumpDriveData(int deviceNumber)
    {
        var dump = _provider.GetFullDump(deviceNumber);

        // Add parsed SAT identity from the raw ATA IDENTIFY buffer (for readability)
        try
        {
            if (dump.TryGetValue("ATA_Identify_Raw", out var ataRaw) && ataRaw is string base64)
            {
                var buf = Convert.FromBase64String(base64);
                var model = ParseAtaString(buf, Ata.WordModel * 2, 40);
                dump["SAT_ATA_IDENTIFY"] = new Dictionary<string, object?>
                {
                    ["Manufacturer"] = InferManufacturer(model),
                    ["Model"] = model,
                    ["SerialNumber"] = ParseAtaString(buf, Ata.WordSerial * 2, 20),
                    ["FirmwareVersion"] = ParseAtaString(buf, Ata.WordFirmware * 2, 8)
                };
            }
        }
        catch { }

        return JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true });
    }

    // ── Manufacturer Inference ──

    internal static string? InferManufacturer(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var m = model.ToUpperInvariant();

        Logger.Info($"InferManufacturer input: \"{m}\"");

        if (m.StartsWith("WDC ") || m.StartsWith("WD ")) return "Western Digital";
        if (m.StartsWith("ST") && char.IsDigit(m.ElementAtOrDefault(2))) return "Seagate";
        if (m.StartsWith("SAMSUNG") || m.StartsWith("MZ")) return "Samsung";
        if (m.StartsWith("TOSHIBA") || m.StartsWith("DT") || m.StartsWith("MQ") || m.StartsWith("HDWD") || m.StartsWith("MG")) return "Toshiba";
        if (m.StartsWith("HGST") || m.StartsWith("HUS") || m.StartsWith("HUH")) return "HGST";
        if (m.StartsWith("HITACHI") || m.StartsWith("HDS") || m.StartsWith("HDT") || m.StartsWith("HDP")) return "Hitachi";
        if (m.StartsWith("CT") && char.IsDigit(m.ElementAtOrDefault(2))) return "Crucial";
        if (m.StartsWith("KINGSTON") || m.StartsWith("SKC") || m.StartsWith("SA400")) return "Kingston";
        if (m.StartsWith("INTEL") || m.StartsWith("SSDPE") || m.StartsWith("SSDSC")) return "Intel";
        if (m.StartsWith("SANDISK") || m.StartsWith("SDSSD")) return "SanDisk";
        if (m.StartsWith("MAXTOR")) return "Maxtor";

        return null;
    }

    // ── Initialize, Format & Assign Letter (static — mutating operation, not on provider) ──

    public static (bool Success, string? DriveLetter, string? Error) InitializeFormatAndAssignLetter(int deviceNumber)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Options.Timeout = TimeSpan.FromSeconds(15);
            scope.Connect();

            // Find the MSFT_Disk for this device
            var diskQuery = new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {deviceNumber}");
            using var diskSearcher = new ManagementObjectSearcher(scope, diskQuery);
            ManagementObject? msftDisk = null;

            foreach (ManagementObject d in diskSearcher.Get())
            {
                msftDisk = d;
                break;
            }

            if (msftDisk == null)
                return (false, null, $"MSFT_Disk not found for disk {deviceNumber}.");

            // 1. Initialize as GPT (PartitionStyle 2 = GPT)
            Logger.Info($"Disk {deviceNumber}: Initializing as GPT...");
            var initParams = msftDisk.GetMethodParameters("Initialize");
            initParams["PartitionStyle"] = (ushort)2; // GPT
            var initResult = msftDisk.InvokeMethod("Initialize", initParams, null);
            var initRet = Convert.ToUInt32(initResult["ReturnValue"]);
            if (initRet != 0)
            {
                Logger.Warning($"Disk {deviceNumber}: Initialize returned {initRet}");
                return (false, null, $"Disk initialize failed (code {initRet}).");
            }

            // 2. Create max-size partition with auto drive letter
            Logger.Info($"Disk {deviceNumber}: Creating partition...");
            var partParams = msftDisk.GetMethodParameters("CreatePartition");
            partParams["UseMaximumSize"] = true;
            partParams["AssignDriveLetter"] = true;
            var partResult = msftDisk.InvokeMethod("CreatePartition", partParams, null);
            var partRet = Convert.ToUInt32(partResult["ReturnValue"]);
            if (partRet != 0)
            {
                Logger.Warning($"Disk {deviceNumber}: CreatePartition returned {partRet}");
                return (false, null, $"Partition creation failed (code {partRet}).");
            }

            // 3. Find the created partition and its volume, then format
            var scope2 = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope2.Options.Timeout = TimeSpan.FromSeconds(15);
            scope2.Connect();

            string? assignedLetter = null;
            var diskQuery2 = new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {deviceNumber}");
            using var diskSearcher2 = new ManagementObjectSearcher(scope2, diskQuery2);

            foreach (ManagementObject disk in diskSearcher2.Get())
            {
                var partitions = disk.GetRelated("MSFT_Partition");
                foreach (ManagementObject partition in partitions)
                {
                    var letterObj = partition["DriveLetter"];
                    if (letterObj == null) continue;
                    var letter = Convert.ToChar(letterObj);
                    if (letter == '\0') continue;

                    assignedLetter = $"{letter}:";

                    try
                    {
                        var volumes = partition.GetRelated("MSFT_Volume");
                        foreach (ManagementObject volume in volumes)
                        {
                            Logger.Info($"Disk {deviceNumber}: Quick formatting volume {assignedLetter} as NTFS...");
                            var fmtParams = volume.GetMethodParameters("Format");
                            fmtParams["FileSystem"] = "NTFS";
                            fmtParams["FileSystemLabel"] = "";
                            fmtParams["Full"] = false;
                            var fmtResult = volume.InvokeMethod("Format", fmtParams, null);
                            var fmtRet = Convert.ToUInt32(fmtResult["ReturnValue"]);
                            if (fmtRet != 0)
                            {
                                Logger.Warning($"Disk {deviceNumber}: Format returned {fmtRet}");
                                return (false, assignedLetter, $"Format failed (code {fmtRet}). Partition created as {assignedLetter}.");
                            }
                            Logger.Info($"Disk {deviceNumber}: Formatted successfully as {assignedLetter}");
                            return (true, assignedLetter, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Disk {deviceNumber}: Volume format failed: {ex.Message}");
                        return (false, assignedLetter, $"Format failed: {ex.Message}. Partition created as {assignedLetter}.");
                    }
                }
            }

            if (assignedLetter != null)
                return (false, assignedLetter, "Partition created but volume not found for formatting.");

            return (false, null, "Partition created but no drive letter was assigned.");
        }
        catch (Exception ex)
        {
            Logger.Error($"InitializeFormatAndAssignLetter failed for disk {deviceNumber}", ex);
            return (false, null, ex.Message);
        }
    }
}
