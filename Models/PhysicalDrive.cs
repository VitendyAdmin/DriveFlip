using System;
using System.Collections.Generic;

namespace DriveFlip.Models;

public class PhysicalDrive
{
    public int DeviceNumber { get; set; }
    public string DevicePath => $"\\\\.\\PhysicalDrive{DeviceNumber}";
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
    public DriveHealthInfo? Health { get; set; }

    public string DisplaySize
    {
        get
        {
            if (SizeBytes >= 1_000_000_000_000)
                return $"{SizeBytes / 1_000_000_000_000.0:F1} TB";
            if (SizeBytes >= 1_000_000_000)
                return $"{SizeBytes / 1_000_000_000.0:F1} GB";
            if (SizeBytes >= 1_000_000)
                return $"{SizeBytes / 1_000_000.0:F1} MB";
            return $"{SizeBytes:N0} bytes";
        }
    }

    public string DisplayName => $"Disk {DeviceNumber}: {Model} ({DisplaySize})";

    public string DriveLettersSummary =>
        DriveLetters.Count > 0 ? string.Join(", ", DriveLetters) : "No volumes";
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
    bool IsFailurePredictive);

public class DriveHealthInfo
{
    // ── Basic (Tier 0) ──
    public string HealthStatus { get; set; } = "N/A";
    public string MediaType { get; set; } = "N/A";
    public string BusType { get; set; } = "N/A";
    public int? SpindleSpeed { get; set; }
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

    // ── Raw SMART Attributes (Tier 2, SATA only) ──
    public List<SmartAttribute> SmartAttributes { get; set; } = new();
    public bool SmartSupported { get; set; }
    public bool SmartDataQueried { get; set; }

    // ── Risk Assessment ──
    public DriveRiskLevel RiskLevel { get; set; } = DriveRiskLevel.Unknown;
    public string RiskSummary { get; set; } = "";

    // ── Display helpers (basic) ──
    public string SpindleSpeedDisplay => SpindleSpeed.HasValue
        ? (SpindleSpeed.Value == 0 ? "SSD (no spindle)" : $"{SpindleSpeed.Value} RPM")
        : "N/A";
    public string TemperatureDisplay => Temperature.HasValue ? $"{Temperature.Value} °C" : "N/A";
    public string PowerOnHoursDisplay => PowerOnHours.HasValue ? $"{PowerOnHours.Value:N0} hours" : "N/A";
    public string ReadErrorsDisplay => ReadErrors.HasValue ? $"{ReadErrors.Value:N0}" : "N/A";
    public string WriteErrorsDisplay => WriteErrors.HasValue ? $"{WriteErrors.Value:N0}" : "N/A";
    public string WearDisplay => Wear.HasValue ? $"{Wear.Value}%" : "N/A";

    // ── Display helpers (extended) ──
    public string ReadErrorsCorrectedDisplay => ReadErrorsCorrected.HasValue ? $"{ReadErrorsCorrected.Value:N0}" : "N/A";
    public string ReadErrorsUncorrectedDisplay => ReadErrorsUncorrected.HasValue ? $"{ReadErrorsUncorrected.Value:N0}" : "N/A";
    public string WriteErrorsCorrectedDisplay => WriteErrorsCorrected.HasValue ? $"{WriteErrorsCorrected.Value:N0}" : "N/A";
    public string WriteErrorsUncorrectedDisplay => WriteErrorsUncorrected.HasValue ? $"{WriteErrorsUncorrected.Value:N0}" : "N/A";
    public string ReadLatencyMaxDisplay => ReadLatencyMax.HasValue ? $"{ReadLatencyMax.Value:N0} ms" : "N/A";
    public string WriteLatencyMaxDisplay => WriteLatencyMax.HasValue ? $"{WriteLatencyMax.Value:N0} ms" : "N/A";
    public string FlushLatencyMaxDisplay => FlushLatencyMax.HasValue ? $"{FlushLatencyMax.Value:N0} ms" : "N/A";
    public string StartStopCycleCountDisplay => StartStopCycleCount.HasValue ? $"{StartStopCycleCount.Value:N0}" : "N/A";
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
