using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DriveFlip.Localization;
using DriveFlip.Services;

namespace DriveFlip.Models;

public class PhysicalDrive
{
    public int DeviceNumber { get; set; }
    public string DevicePath => $"\\\\.\\PhysicalDrive{DeviceNumber}";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "Unknown";
    public string SerialNumber { get; set; } = "";
    public string InterfaceType { get; set; } = "Unknown";
    public string MediaType { get; set; } = "Unknown";
    public long SizeBytes { get; set; }
    public int BytesPerSector { get; set; } = 512;
    public long TotalSectors => BytesPerSector > 0 ? SizeBytes / BytesPerSector : 0;
    public List<string> Partitions { get; set; } = new();
    public List<string> DriveLetters { get; set; } = new();
    public bool IsSystemDrive { get; set; }
    public bool IsRemovable { get; set; }
    public string FirmwareVersion { get; set; } = "";
    public string Status { get; set; } = "OK";
    public string SupportedFeatures { get; set; } = "";
    public string NegotiatedSpeed { get; set; } = "";
    public DriveHealthInfo? Health { get; set; }
    public string PartNumber { get; set; } = "";
    public string ManufactureDate { get; set; } = "";
    public int PhysicalSectorSize { get; set; }
    public int LogicalSectorSize { get; set; }
    public string PartitionStyle { get; set; } = "";
    public string UsbBridgeChip { get; set; } = "";
    public string PnpDeviceId { get; set; } = "";

    public long UsedBytes { get; set; }

    public string SectorSizeDisplay => DisplayFormatter.FormatSectorSize(PhysicalSectorSize, LogicalSectorSize);

    public string DisplaySize => DisplayFormatter.FormatSize(SizeBytes);

    public string DisplayName => $"Disk {DeviceNumber}: {Model} ({DisplaySize})";

    public double UsedPercent => SizeBytes > 0 ? (double)UsedBytes / SizeBytes * 100.0 : 0;

    public string CapacitySummary
    {
        get
        {
            if (DriveLetters.Count == 0) return $"{DisplaySize}";
            if (UsedBytes <= 0) return $"{DisplaySize}";
            return $"{DisplayFormatter.FormatSize(UsedBytes)} / {DisplaySize} {Loc.Get("DriveUsedSuffix")}";
        }
    }

    public string SerialSuffix =>
        SerialNumber.Length >= 4 ? $"s/n \u2026{SerialNumber[^4..]}" : "";

    public string DriveLettersSummary =>
        DriveLetters.Count > 0 ? string.Join(", ", DriveLetters) : Loc.Get("DriveNoVolumes");

    public string PartitionSummary
    {
        get
        {
            if (Partitions.Count == 0) return Loc.Get("DriveNoPartitions");
            var style = !string.IsNullOrEmpty(PartitionStyle) ? PartitionStyle : "";
            var label = Partitions.Count == 1
                ? Loc.Get("DrivePartitionSingular")
                : Loc.Format("DrivePartitionsPlural", Partitions.Count);
            return style.Length > 0 ? $"{label} ({style})" : label;
        }
    }

    public bool HasVolumes => DriveLetters.Count > 0;
}

public enum DriveRiskLevel
{
    Unknown,
    Good,
    Warning,
    Critical
}

public record SmartAttribute(
    byte Id,
    string Name,
    byte CurrentValue,
    byte WorstValue,
    long RawValue,
    bool IsFailurePredictive,
    byte Threshold = 0)
{
    public bool IsThresholdFailing => Threshold > 0 && CurrentValue <= Threshold;
}

public class DriveHealthInfo
{
    // ── Basic (Tier 0) ──
    public string HealthStatus { get; set; } = "N/A";
    public string MediaType { get; set; } = "N/A";
    public string BusType { get; set; } = "N/A";
    public int? Temperature { get; set; }
    public long? PowerOnHours { get; set; }
    public long? ReadErrors { get; set; }
    public long? WriteErrors { get; set; }
    public int? Wear { get; set; }

    // ── Extended Reliability (Tier 1) ──
    public long? ReadErrorsCorrected { get; set; }
    public long? ReadErrorsUncorrected { get; set; }
    public long? WriteErrorsCorrected { get; set; }
    public long? WriteErrorsUncorrected { get; set; }
    public long? ReadLatencyMax { get; set; }
    public long? WriteLatencyMax { get; set; }
    public long? FlushLatencyMax { get; set; }
    public long? StartStopCycleCount { get; set; }

    // ── Additional Info ──
    public string ManufactureDate { get; set; } = "";
    public int? TemperatureMax { get; set; }
    public bool? IsWriteCacheEnabled { get; set; }
    public bool? IsPowerProtected { get; set; }
    public long? LoadUnloadCycleCount { get; set; }
    public long? LoadUnloadCycleCountMax { get; set; }
    public long? StartStopCycleCountMax { get; set; }

    // ── NVMe Health Log ──
    public bool IsNvme { get; set; }
    public int? NvmeAvailableSpare { get; set; }
    public int? NvmeAvailableSpareThreshold { get; set; }
    public int? NvmePercentageUsed { get; set; }
    public long NvmeDataUnitsWritten { get; set; }
    public long NvmeDataUnitsRead { get; set; }
    public long NvmePowerCycles { get; set; }
    public long NvmeUnsafeShutdowns { get; set; }
    public long NvmeMediaErrors { get; set; }
    public long NvmeControllerBusyTime { get; set; }
    public int? NvmeTemperatureSensor1 { get; set; }
    public int? NvmeTemperatureSensor2 { get; set; }

    // ── Raw SMART Attributes (Tier 2, SATA only) ──
    public List<SmartAttribute> SmartAttributes { get; set; } = new();
    public bool SmartSupported { get; set; }
    public bool SmartDataQueried { get; set; }

    // ── Risk Assessment ──
    public DriveRiskLevel RiskLevel { get; set; } = DriveRiskLevel.Unknown;
    public string RiskSummary { get; set; } = "";

    // ── Display helpers (basic) ──
    public string TemperatureDisplay => DisplayFormatter.FormatTemperature(Temperature);
    public string PowerOnHoursDisplay => DisplayFormatter.FormatPowerOnHours(PowerOnHours);
    public string ReadErrorsDisplay => DisplayFormatter.FormatCount(ReadErrors);
    public string WriteErrorsDisplay => DisplayFormatter.FormatCount(WriteErrors);
    public string WearDisplay => DisplayFormatter.FormatPercent(Wear);

    // ── Display helpers (extended) ──
    public string ReadErrorsCorrectedDisplay => DisplayFormatter.FormatCount(ReadErrorsCorrected);
    public string ReadErrorsUncorrectedDisplay => DisplayFormatter.FormatCount(ReadErrorsUncorrected);
    public string WriteErrorsCorrectedDisplay => DisplayFormatter.FormatCount(WriteErrorsCorrected);
    public string WriteErrorsUncorrectedDisplay => DisplayFormatter.FormatCount(WriteErrorsUncorrected);
    public string ReadLatencyMaxDisplay => DisplayFormatter.FormatLatency(ReadLatencyMax);
    public string WriteLatencyMaxDisplay => DisplayFormatter.FormatLatency(WriteLatencyMax);
    public string FlushLatencyMaxDisplay => DisplayFormatter.FormatLatency(FlushLatencyMax);
    public string StartStopCycleCountDisplay => DisplayFormatter.FormatCount(StartStopCycleCount);

    // ── Display helpers (enhanced) ──
    public string TemperatureDisplayEnhanced => DisplayFormatter.FormatTemperatureEnhanced(Temperature, TemperatureMax);
    public string WriteCacheDisplay => DisplayFormatter.FormatBool(IsWriteCacheEnabled, Loc.Get("FormatEnabled"), Loc.Get("FormatDisabled"));
    public string PowerProtectionDisplay => DisplayFormatter.FormatBool(IsPowerProtected);
    public string LoadUnloadCycleCountDisplay => DisplayFormatter.FormatCountWithMax(LoadUnloadCycleCount, LoadUnloadCycleCountMax);
    public string StartStopCycleCountEnhancedDisplay => DisplayFormatter.FormatCountWithMax(StartStopCycleCount, StartStopCycleCountMax);

    // ── Display helpers (NVMe) ──
    public string NvmeAvailableSpareDisplay => DisplayFormatter.FormatPercentWithThreshold(NvmeAvailableSpare, NvmeAvailableSpareThreshold);
    public string NvmePercentageUsedDisplay => DisplayFormatter.FormatPercent(NvmePercentageUsed);
    public string NvmeDataWrittenDisplay => DisplayFormatter.FormatNvmeDataUnits(NvmeDataUnitsWritten);
    public string NvmeDataReadDisplay => DisplayFormatter.FormatNvmeDataUnits(NvmeDataUnitsRead);
    public string NvmePowerCyclesDisplay => DisplayFormatter.FormatCount(NvmePowerCycles > 0 ? NvmePowerCycles : null);
    public string NvmeUnsafeShutdownsDisplay => DisplayFormatter.FormatCount((long?)NvmeUnsafeShutdowns);
    public string NvmeMediaErrorsDisplay => DisplayFormatter.FormatCount((long?)NvmeMediaErrors);
    public string NvmeControllerBusyTimeDisplay => DisplayFormatter.FormatDuration(NvmeControllerBusyTime > 0 ? NvmeControllerBusyTime : null);
}

public enum OperationType
{
    SurfaceCheck,
    SecureWipe,
    VerifyWipe
}

public enum WipeMethod
{
    ZeroFill,
    RandomFill,
    RandomThenZero
}

public enum WipeMode
{
    SmartWipe,
    FullWipe
}

public class WipeSettings
{
    public int HeadTailSizeGB { get; set; } = 10;
    public int SurfaceCheckDurationMinutes { get; set; } = 15;
    public int ScatterDurationMinutes { get; set; } = 15;
    public int NumberOfPasses { get; set; } = 1;
    public WipeMode WipeMode { get; set; } = WipeMode.SmartWipe;
}

public class SectorResult
{
    public long SectorIndex { get; set; }
    public bool ReadSuccess { get; set; }
    public bool HasData { get; set; }
    public bool WriteSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

public class OperationProgress
{
    public int DriveNumber { get; set; }
    public OperationType Operation { get; set; }
    public double PercentComplete { get; set; }
    public long SectorsProcessed { get; set; }
    public long TotalSectorsToProcess { get; set; }
    public long ErrorCount { get; set; }
    public long DataSectorsFound { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan EstimatedRemaining { get; set; }
    public double SpeedMBps { get; set; }
    public string StatusMessage { get; set; } = "";
    public bool IsComplete { get; set; }
    public bool WasCancelled { get; set; }
}

public class SurfaceCheckReport
{
    public int DriveNumber { get; set; }
    public string DriveModel { get; set; } = "";
    public string DriveSerial { get; set; } = "";
    public long DriveSizeBytes { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long TotalSectorsSampled { get; set; }
    public long TotalSectors { get; set; }
    public double CoveragePercent => TotalSectors > 0 ? (double)TotalSectorsSampled / TotalSectors * 100 : 0;
    public long ReadErrors { get; set; }
    public long SectorsWithData { get; set; }
    public long SectorsEmpty { get; set; }
    public double DataPresencePercent =>
        TotalSectorsSampled > 0 ? (double)SectorsWithData / TotalSectorsSampled * 100 : 0;
    public bool Passed => ReadErrors == 0;
    public List<long> BadSectors { get; set; } = new();
}

public class WipeReport
{
    public int DriveNumber { get; set; }
    public string DriveModel { get; set; } = "";
    public long DriveSizeBytes { get; set; }
    public WipeMethod Method { get; set; }
    public WipeMode Mode { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long SectorsWritten { get; set; }
    public long WriteErrors { get; set; }
    public long VerificationErrors { get; set; }
    public bool VerificationPassed { get; set; }
    public bool Completed { get; set; }
}

public class ListingField : INotifyPropertyChanged
{
    public string Key { get; }
    public string Label { get; }
    public Func<PhysicalDrive, string?> GetValue { get; }

    private bool _isIncluded;
    public bool IsIncluded
    {
        get => _isIncluded;
        set { _isIncluded = value; OnPropertyChanged(); }
    }

    public ListingField(string key, string label, bool isIncluded, Func<PhysicalDrive, string?> getValue)
    {
        Key = key;
        Label = label;
        _isIncluded = isIncluded;
        GetValue = getValue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static List<ListingField> CreateDefaultFields() =>
    [
        new("status", Loc.Get("FieldStatus"), true, d => d.Health?.RiskLevel switch
        {
            DriveRiskLevel.Good => Loc.Get("RiskGoodToSell"),
            DriveRiskLevel.Warning => Loc.Get("RiskReviewNeeded"),
            DriveRiskLevel.Critical => Loc.Get("RiskDoNotSell"),
            _ => Loc.Get("RiskNotAssessed")
        }),
        new("manufacturer", Loc.Get("FieldManufacturer"), true, d =>
            string.IsNullOrEmpty(d.Manufacturer) ? null : d.Manufacturer),
        new("model", Loc.Get("FieldModel"), true, d => d.Model),
        new("capacity", Loc.Get("FieldCapacity"), true, d => d.DisplaySize),
        new("interface", Loc.Get("FieldInterface"), true, d => d.InterfaceType),
        new("transfer", Loc.Get("FieldTransferMode"), true, d =>
            string.IsNullOrEmpty(d.NegotiatedSpeed) ? null : d.NegotiatedSpeed),
        new("serial", Loc.Get("FieldSerialNumber"), false, d =>
            string.IsNullOrEmpty(d.SerialNumber) ? null : d.SerialNumber),
        new("firmware", Loc.Get("FieldFirmware"), false, d =>
            string.IsNullOrEmpty(d.FirmwareVersion) ? null : d.FirmwareVersion),
        new("health", Loc.Get("FieldHealth"), true, d => d.Health?.HealthStatus),
        new("temperature", Loc.Get("FieldTemperature"), true, d => d.Health?.TemperatureDisplay),
        new("powerOn", Loc.Get("FieldPowerOnHours"), true, d => d.Health?.PowerOnHoursDisplay),
        new("readErrors", Loc.Get("FieldReadErrors"), true, d => d.Health?.ReadErrorsDisplay),
        new("writeErrors", Loc.Get("FieldWriteErrors"), true, d => d.Health?.WriteErrorsDisplay),
        new("ssdWear", Loc.Get("FieldSsdWear"), false, d => d.Health?.Wear.HasValue == true ? d.Health.WearDisplay : null),
        new("mediaType", Loc.Get("FieldMediaType"), false, d => d.Health?.MediaType),
        new("features", Loc.Get("FieldFeatures"), false, d =>
            string.IsNullOrEmpty(d.SupportedFeatures) ? null : d.SupportedFeatures),
        new("partNumber", Loc.Get("FieldPartNumber"), false, d =>
            string.IsNullOrEmpty(d.PartNumber) ? null : d.PartNumber),
        new("mfgDate", Loc.Get("FieldManufactureDate"), false, d =>
            string.IsNullOrEmpty(d.ManufactureDate) ? null : d.ManufactureDate),
        new("sectorSize", Loc.Get("FieldSectorSize"), false, d =>
            d.PhysicalSectorSize > 0 ? d.SectorSizeDisplay : null),
        new("partStyle", Loc.Get("FieldPartitionStyle"), false, d =>
            string.IsNullOrEmpty(d.PartitionStyle) ? null : d.PartitionStyle),
    ];
}
