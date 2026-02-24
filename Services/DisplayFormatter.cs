using DriveFlip.Localization;

namespace DriveFlip.Services;

public static class DisplayFormatter
{
    public static string NotAvailable => Loc.Get("FormatNotAvailable");

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000_000_000)
            return $"{bytes / 1_000_000_000_000.0:F1} TB";
        if (bytes >= 1_000_000_000)
            return $"{bytes / 1_000_000_000.0:F1} GB";
        if (bytes >= 1_000_000)
            return $"{bytes / 1_000_000.0:F1} MB";
        return $"{bytes:N0} bytes";
    }

    public static string FormatNvmeDataUnits(long units)
    {
        if (units <= 0) return NotAvailable;
        double bytes = units * 512000.0;
        if (bytes >= 1e15) return $"{bytes / 1e15:F2} PB";
        if (bytes >= 1e12) return $"{bytes / 1e12:F2} TB";
        if (bytes >= 1e9) return $"{bytes / 1e9:F2} GB";
        return $"{bytes / 1e6:F2} MB";
    }

    public static string FormatTemperature(int? temp) =>
        temp.HasValue ? $"{temp.Value} °C" : NotAvailable;

    public static string FormatTemperatureEnhanced(int? temp, int? max) =>
        temp.HasValue
            ? (max.HasValue ? $"{temp.Value} °C (max {max.Value} °C)" : $"{temp.Value} °C")
            : NotAvailable;

    public static string FormatCount(long? count) =>
        count.HasValue ? $"{count.Value:N0}" : NotAvailable;

    public static string FormatCountWithMax(long? count, long? max) =>
        count.HasValue
            ? (max.HasValue && max.Value > 0
                ? $"{count.Value:N0} / {max.Value:N0}"
                : $"{count.Value:N0}")
            : NotAvailable;

    public static string FormatPercent(int? pct) =>
        pct.HasValue ? $"{pct.Value}%" : NotAvailable;

    public static string FormatPercentWithThreshold(int? pct, int? threshold) =>
        pct.HasValue
            ? (threshold.HasValue
                ? $"{pct.Value}% (threshold {threshold.Value}%)"
                : $"{pct.Value}%")
            : NotAvailable;

    public static string FormatBool(bool? value, string? trueText = null, string? falseText = null) =>
        value.HasValue ? (value.Value ? (trueText ?? Loc.Get("FormatYes")) : (falseText ?? Loc.Get("FormatNo"))) : NotAvailable;

    public static string FormatSectorSize(int physical, int logical)
    {
        if (physical == 0 && logical == 0) return NotAvailable;
        if (physical == logical)
            return physical == 4096 ? "4Kn (4096)" : $"{physical}";
        if (logical == 512 && physical == 4096)
            return Loc.Get("FormatSector512e");
        return $"{logical} / {physical}";
    }

    public static string FormatLatency(long? ms) =>
        ms.HasValue ? $"{ms.Value:N0} ms" : NotAvailable;

    public static string FormatDuration(long? minutes) =>
        minutes.HasValue ? $"{minutes.Value:N0} min" : NotAvailable;

    public static string FormatPowerOnHours(long? hours) =>
        hours.HasValue ? $"{hours.Value:N0} hours" : NotAvailable;
}
