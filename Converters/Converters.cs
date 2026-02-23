using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
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
                DriveRiskLevel.Good => "GOOD TO SELL",
                DriveRiskLevel.Warning => "REVIEW NEEDED",
                DriveRiskLevel.Critical => "DO NOT SELL",
                _ => "UNKNOWN"
            };
        }
        return "UNKNOWN";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class LicenseTooltipConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool unlicensed && unlicensed)
            return "License required to use this feature";
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
