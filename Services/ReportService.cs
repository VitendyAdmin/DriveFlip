using System;
using System.IO;
using System.Text;
using DriveFlip.Localization;
using DriveFlip.Models;

namespace DriveFlip.Services;

public static class ReportService
{
    public static string GenerateSurfaceCheckReport(SurfaceCheckReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════╗");
        sb.AppendLine($"║       {Loc.Get("ReportSurfaceCheckTitle"),-45}║");
        sb.AppendLine("╚══════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportDrive"))} Disk {report.DriveNumber} - {report.DriveModel}");
        if (!string.IsNullOrEmpty(report.DriveSerial))
            sb.AppendLine($"  {PadLabel(Loc.Get("ReportSerial"))} {report.DriveSerial}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportSize"))} {DisplayFormatter.FormatSize(report.DriveSizeBytes)}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportDate"))} {report.StartTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportDuration"))} {(report.EndTime - report.StartTime):hh\\:mm\\:ss}");
        sb.AppendLine();
        sb.AppendLine($"  ── {Loc.Get("ReportResults")} ──────────────────────────────────────────");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportSectorsSampled"))} {report.TotalSectorsSampled:N0} of {report.TotalSectors:N0}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportCoverage"))} {report.CoveragePercent:F2}%");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportReadErrors"))} {report.ReadErrors:N0}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportSectorsWithData"))} {report.SectorsWithData:N0} ({report.DataPresencePercent:F1}%)");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportEmptySectors"))} {report.SectorsEmpty:N0}");
        sb.AppendLine();

        if (report.ReadErrors > 0)
            sb.AppendLine($"  {Loc.Get("ReportWarnReadErrors")}");
        else
            sb.AppendLine($"  {Loc.Get("ReportOkNoErrors")}");

        if (report.DataPresencePercent > 5)
        {
            sb.AppendLine();
            sb.AppendLine($"  {Loc.Format("ReportNoteDataPresence", $"{report.DataPresencePercent:F1}")}");
        }
        else if (report.DataPresencePercent > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  {Loc.Format("ReportInfoMinimalData", $"{report.DataPresencePercent:F1}")}");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine($"  {Loc.Get("ReportOkClean")}");
        }

        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    private static string PadLabel(string label) => label.PadRight(17);

    public static string GenerateWipeReport(WipeReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════╗");
        sb.AppendLine($"║       {Loc.Get("ReportWipeTitle"),-45}║");
        sb.AppendLine("╚══════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportDrive"))} Disk {report.DriveNumber} - {report.DriveModel}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportSize"))} {DisplayFormatter.FormatSize(report.DriveSizeBytes)}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportWipeMode"))} {FormatWipeMode(report.Mode)}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportFillMethod"))} {FormatWipeMethod(report.Method)}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportDate"))} {report.StartTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportDuration"))} {(report.EndTime - report.StartTime):hh\\:mm\\:ss}");
        sb.AppendLine();
        sb.AppendLine($"  ── {Loc.Get("ReportResults")} ──────────────────────────────────────────");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportSectorsWritten"))} {report.SectorsWritten:N0}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportWriteErrors"))} {report.WriteErrors:N0}");
        sb.AppendLine($"  {PadLabel(Loc.Get("ReportCompleted"))} {(report.Completed ? Loc.Get("ReportCompletedYes") : Loc.Get("ReportCompletedNo"))}");

        if (report.VerificationPassed)
            sb.AppendLine($"  {Loc.Get("ReportVerificationPassed")}");
        else if (report.VerificationErrors > 0)
            sb.AppendLine($"  {Loc.Format("ReportVerificationFailed", report.VerificationErrors.ToString("N0"))}");

        sb.AppendLine();

        if (report.Completed && report.WriteErrors == 0)
            sb.AppendLine($"  {Loc.Get("ReportWipeSuccess")}");
        else if (report.WriteErrors > 0)
            sb.AppendLine($"  {Loc.Get("ReportWipeWithErrors")}");

        sb.AppendLine();
        sb.AppendLine("══════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    public static void SaveReport(string reportText, string filePath)
    {
        File.WriteAllText(filePath, reportText, Encoding.UTF8);
    }

    private static string FormatWipeMethod(WipeMethod method) => method switch
    {
        WipeMethod.ZeroFill => Loc.Get("ReportMethodZero"),
        WipeMethod.RandomFill => Loc.Get("ReportMethodRandom"),
        WipeMethod.RandomThenZero => Loc.Get("ReportMethodRandomZero"),
        _ => method.ToString()
    };

    private static string FormatWipeMode(WipeMode mode) => mode switch
    {
        WipeMode.SmartWipe => Loc.Get("ReportModeSmartDetail"),
        WipeMode.FullWipe => Loc.Get("ReportModeFullDetail"),
        _ => mode.ToString()
    };
}
