using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DriveFlip.Localization;
using DriveFlip.Models;

namespace DriveFlip.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        bool invert = parameter?.ToString() == "Invert";
        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

public class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is double percent &&
            values[1] is double totalWidth)
        {
            return Math.Max(0, totalWidth * percent / 100.0);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DataPresenceToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            if (percent < 1) return new SolidColorBrush(Color.FromRgb(76, 175, 80));   // Green
            if (percent < 20) return new SolidColorBrush(Color.FromRgb(255, 193, 7));  // Amber
            return new SolidColorBrush(Color.FromRgb(244, 67, 54));                     // Red
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class HealthToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long errors)
        {
            if (errors == 0) return new SolidColorBrush(Color.FromRgb(76, 175, 80));
            if (errors < 10) return new SolidColorBrush(Color.FromRgb(255, 193, 7));
            return new SolidColorBrush(Color.FromRgb(244, 67, 54));
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString() == "Invert";
        bool isNull = value is null;
        if (invert) isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
            return Enum.Parse(targetType, parameter.ToString()!);
        return Binding.DoNothing;
    }
}

public class RiskLevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DriveRiskLevel level)
        {
            return level switch
            {
                DriveRiskLevel.Good => new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                DriveRiskLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                DriveRiskLevel.Critical => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class RiskLevelToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DriveRiskLevel level)
        {
            return level switch
            {
                DriveRiskLevel.Good => Loc.Get("RiskGoodToSell"),
                DriveRiskLevel.Warning => Loc.Get("RiskReviewNeeded"),
                DriveRiskLevel.Critical => Loc.Get("RiskDoNotSell"),
                _ => Loc.Get("RiskUnknown")
            };
        }
        return Loc.Get("RiskUnknown");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LicenseTooltipConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool unlicensed && unlicensed)
            return Loc.Get("LicenseTooltip");
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class PhaseStatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush DoneBrush = new(Color.FromRgb(76, 175, 80));    // Green
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(74, 138, 196)); // AccentBlue
    private static readonly SolidColorBrush PendingBrush = new(Color.FromRgb(80, 80, 106)); // Dim

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DriveFlip.ViewModels.PhaseStatus status)
        {
            return status switch
            {
                DriveFlip.ViewModels.PhaseStatus.Done => DoneBrush,
                DriveFlip.ViewModels.PhaseStatus.Active => ActiveBrush,
                _ => PendingBrush
            };
        }
        return PendingBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class PhaseStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DriveFlip.ViewModels.PhaseStatus status)
        {
            return status switch
            {
                DriveFlip.ViewModels.PhaseStatus.Done => "\u2713",     // checkmark
                DriveFlip.ViewModels.PhaseStatus.Active => "\u25B6",   // play triangle
                _ => "\u2022"                                           // bullet
            };
        }
        return "\u2022";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NonEmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class AllTrueMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.All(v => v is bool b && b);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns Visible when IsComplete=true AND IsRunning=false.
/// </summary>
public class CompletedNotRunningConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is bool isComplete && values[1] is bool isRunning)
            return isComplete && !isRunning ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ── Drive Log Converters ──

public class DriveLogStatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(76, 175, 80));     // Green
    private static readonly SolidColorBrush InUseBrush = new(Color.FromRgb(74, 138, 196));      // Blue
    private static readonly SolidColorBrush SoldBrush = new(Color.FromRgb(128, 128, 128));      // Gray
    private static readonly SolidColorBrush FailedBrush = new(Color.FromRgb(244, 67, 54));      // Red
    private static readonly SolidColorBrush RmaBrush = new(Color.FromRgb(255, 193, 7));         // Amber
    private static readonly SolidColorBrush ArchivedBrush = new(Color.FromRgb(100, 100, 130));  // Muted

    private static readonly SolidColorBrush ActiveBg = new(Color.FromArgb(60, 76, 175, 80));
    private static readonly SolidColorBrush InUseBg = new(Color.FromArgb(60, 74, 138, 196));
    private static readonly SolidColorBrush SoldBg = new(Color.FromArgb(60, 128, 128, 128));
    private static readonly SolidColorBrush FailedBg = new(Color.FromArgb(60, 244, 67, 54));
    private static readonly SolidColorBrush RmaBg = new(Color.FromArgb(60, 255, 193, 7));
    private static readonly SolidColorBrush ArchivedBg = new(Color.FromArgb(60, 100, 100, 130));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool bg = parameter?.ToString() == "Background";
        if (value is DriveLogStatus status)
        {
            return status switch
            {
                DriveLogStatus.Active => bg ? ActiveBg : ActiveBrush,
                DriveLogStatus.InUse => bg ? InUseBg : InUseBrush,
                DriveLogStatus.Sold => bg ? SoldBg : SoldBrush,
                DriveLogStatus.Failed => bg ? FailedBg : FailedBrush,
                DriveLogStatus.RMA => bg ? RmaBg : RmaBrush,
                DriveLogStatus.Archived => bg ? ArchivedBg : ArchivedBrush,
                _ => (object)(bg ? ArchivedBg : ArchivedBrush)
            };
        }
        return bg ? ArchivedBg : ArchivedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DriveLogStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DriveLogStatus status)
        {
            return status switch
            {
                DriveLogStatus.Active => Loc.Get("DriveLogStatusActive"),
                DriveLogStatus.InUse => Loc.Get("DriveLogStatusInUse"),
                DriveLogStatus.Sold => Loc.Get("DriveLogStatusSold"),
                DriveLogStatus.Failed => Loc.Get("DriveLogStatusFailed"),
                DriveLogStatus.RMA => Loc.Get("DriveLogStatusRMA"),
                DriveLogStatus.Archived => Loc.Get("DriveLogStatusArchived"),
                _ => status.ToString()
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FilePathToThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string fileName || string.IsNullOrEmpty(fileName))
            return null;

        try
        {
            var photosRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DriveFlip", "photos");

            var dirs = Directory.Exists(photosRoot) ? Directory.GetDirectories(photosRoot) : [];
            foreach (var dir in dirs)
            {
                var fullPath = Path.Combine(dir, fileName);
                if (!File.Exists(fullPath)) continue;

                // Use BitmapFrame which respects EXIF orientation metadata
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

                // Scale down to thumbnail size
                double scale = 180.0 / Math.Max(frame.PixelWidth, frame.PixelHeight);
                if (scale < 1.0)
                {
                    var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    scaled.Freeze();
                    return scaled;
                }

                frame.Freeze();
                return frame;
            }
        }
        catch { /* thumbnail load failure is non-critical */ }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NullToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToPassFailConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Loc.Get("BadgePass") : Loc.Get("BadgeFail");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToPassFailColorConverter : IValueConverter
{
    private static readonly SolidColorBrush PassBrush = new(Color.FromRgb(76, 175, 80));
    private static readonly SolidColorBrush FailBrush = new(Color.FromRgb(244, 67, 54));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? PassBrush : FailBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
