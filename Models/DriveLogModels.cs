using System;
using System.Collections.Generic;

namespace DriveFlip.Models;

public enum DriveLogStatus
{
    Active,
    InUse,
    Sold,
    Failed,
    RMA,
    Archived
}

public class DriveLogEntry
{
    // ── Identity ──
    public string SerialNumber { get; set; } = "";
    public string Model { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public long SizeBytes { get; set; }
    public string MediaType { get; set; } = "";
    public string InterfaceType { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
    public string PartNumber { get; set; } = "";

    // ── Timestamps ──
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? LastOperationUtc { get; set; }

    // ── Status ──
    public DriveLogStatus Status { get; set; } = DriveLogStatus.Active;

    // ── Health Snapshot ──
    public LogHealthSnapshot? HealthSnapshot { get; set; }

    // ── History ──
    public List<LogOperationRecord> Operations { get; set; } = new();
    public List<LogNote> Notes { get; set; } = new();
    public List<string> Photos { get; set; } = new();

    public string DisplaySize => Services.DisplayFormatter.FormatSize(SizeBytes);
}

public class LogHealthSnapshot
{
    public string HealthStatus { get; set; } = "";
    public DriveRiskLevel RiskLevel { get; set; } = DriveRiskLevel.Unknown;
    public int? Temperature { get; set; }
    public long? PowerOnHours { get; set; }
    public long? ReadErrors { get; set; }
    public long? WriteErrors { get; set; }
    public int? Wear { get; set; }
    public DateTime CapturedUtc { get; set; }
}

public class LogOperationRecord
{
    public OperationType Type { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool Passed { get; set; }
    public string Summary { get; set; } = "";

    // Surface check metrics
    public long? SectorsSampled { get; set; }
    public double? CoveragePercent { get; set; }
    public long? ReadErrors { get; set; }
    public double? DataPresencePercent { get; set; }

    // Wipe metrics
    public long? SectorsWritten { get; set; }
    public long? WriteErrors { get; set; }
    public WipeMethod? WipeMethod { get; set; }
    public WipeMode? WipeMode { get; set; }
}

public class LogNote
{
    public DateTime TimestampUtc { get; set; }
    public string Text { get; set; } = "";
}
