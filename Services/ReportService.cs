using System;
using System.IO;
using System.Text;
using DriveFlip.Models;

namespace DriveFlip.Services;

public static class ReportService
{
    public static string GenerateSurfaceCheckReport(SurfaceCheckReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════╗");
        sb.AppendLine("║       DRIVEFLIP — SURFACE CHECK REPORT             ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"  Drive:           Disk {report.DriveNumber} - {report.DriveModel}");
        if (!string.IsNullOrEmpty(report.DriveSerial))
            sb.AppendLine($"  Serial:          {report.DriveSerial}");
        sb.AppendLine($"  Size:            {FormatBytes(report.DriveSizeBytes)}");
        sb.AppendLine($"  Date:            {report.StartTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Duration:        {(report.EndTime - report.StartTime):hh\\:mm\\:ss}");
        sb.AppendLine();
        sb.AppendLine("  ── Results ──────────────────────────────────────────");
        sb.AppendLine($"  Sectors Sampled: {report.TotalSectorsSampled:N0} of {report.TotalSectors:N0}");
        sb.AppendLine($"  Coverage:        {report.CoveragePercent:F2}%");
        sb.AppendLine($"  Read Errors:     {report.ReadErrors:N0}");
        sb.AppendLine($"  Sectors w/ Data: {report.SectorsWithData:N0} ({report.DataPresencePercent:F1}%)");
        sb.AppendLine($"  Empty Sectors:   {report.SectorsEmpty:N0}");
        sb.AppendLine();

        if (report.ReadErrors > 0)
        {
            sb.AppendLine("  WARNING: Read errors detected. Drive may have bad sectors.");
            sb.AppendLine("    Consider replacing this drive before selling.");
        }
        else
        {
            sb.AppendLine("  OK: No read errors detected. Drive surface appears healthy.");
        }

        if (report.DataPresencePercent > 5)
        {
            sb.AppendLine();
            sb.AppendLine($"  NOTE: {report.DataPresencePercent:F1}% of sampled sectors contain data.");
            sb.AppendLine("    Recommend running a wipe before selling.");
        }
        else if (report.DataPresencePercent > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  INFO: {report.DataPresencePercent:F1}% of sampled sectors contain data (minimal).");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("  OK: Drive appears to be clean — no data found in sampled sectors.");
        }

        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    public static string GenerateWipeReport(WipeReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════╗");
        sb.AppendLine("║       DRIVEFLIP — WIPE REPORT                      ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"  Drive:           Disk {report.DriveNumber} - {report.DriveModel}");
        sb.AppendLine($"  Size:            {FormatBytes(report.DriveSizeBytes)}");
        sb.AppendLine($"  Wipe Mode:       {FormatWipeMode(report.Mode)}");
        sb.AppendLine($"  Fill Method:     {FormatWipeMethod(report.Method)}");
        sb.AppendLine($"  Date:            {report.StartTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Duration:        {(report.EndTime - report.StartTime):hh\\:mm\\:ss}");
        sb.AppendLine();
        sb.AppendLine("  ── Results ──────────────────────────────────────────");
        sb.AppendLine($"  Sectors Written: {report.SectorsWritten:N0}");
        sb.AppendLine($"  Write Errors:    {report.WriteErrors:N0}");
        sb.AppendLine($"  Completed:       {(report.Completed ? "Yes" : "No — Interrupted")}");

        if (report.VerificationPassed)
            sb.AppendLine("  Verification:    PASSED — all sectors verified clean");
        else if (report.VerificationErrors > 0)
            sb.AppendLine($"  Verification:    {report.VerificationErrors:N0} sectors failed verification");

        sb.AppendLine();

        if (report.Completed && report.WriteErrors == 0)
            sb.AppendLine("  OK: Drive has been wiped successfully.");
        else if (report.WriteErrors > 0)
            sb.AppendLine("  WARNING: Wipe completed with errors. Some sectors may not have been overwritten.");

        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    public static void SaveReport(string reportText, string filePath)
    {
        File.WriteAllText(filePath, reportText, Encoding.UTF8);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000_000)
            return $"{bytes / 1_000_000_000_000.0:F2} TB";
        if (bytes >= 1_000_000_000)
            return $"{bytes / 1_000_000_000.0:F2} GB";
        return $"{bytes / 1_000_000.0:F2} MB";
    }

    private static string FormatWipeMethod(WipeMethod method) => method switch
    {
        WipeMethod.ZeroFill => "Zero Fill (single pass)",
        WipeMethod.RandomFill => "Random Data Fill (single pass)",
        WipeMethod.RandomThenZero => "Random + Zero Fill (two passes)",
        _ => method.ToString()
    };

    private static string FormatWipeMode(WipeMode mode) => mode switch
    {
        WipeMode.SmartWipe => "Smart Wipe (head + tail + scatter)",
        WipeMode.FullWipe => "Full Wipe (every sector)",
        _ => mode.ToString()
    };
}
