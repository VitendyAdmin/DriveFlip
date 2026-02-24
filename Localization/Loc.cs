using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using DriveFlip.Resources;

namespace DriveFlip.Localization;

/// <summary>
/// Static helper for accessing localized strings from C# code.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Rm = Strings.ResourceManager;

    public static string Get(string key)
        => Rm.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";

    public static string Format(string key, params object[] args)
        => string.Format(Get(key), args);

    public static void SetLanguage(string cultureCode)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(cultureCode);
        LocalizationSource.Instance.NotifyAllChanged();
    }

    public static string CurrentLanguageCode => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("fr", "Fran\u00e7ais"),
        new("es", "Espa\u00f1ol"),
        new("hu", "Magyar"),
        new("pl", "Polski"),
        new("pt", "Portugu\u00eas")
    ];
}

public record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Singleton that WPF bindings target. When language changes, raises PropertyChanged
/// so all {loc:Loc} bindings in XAML re-evaluate.
/// </summary>
public class LocalizationSource : INotifyPropertyChanged
{
    public static LocalizationSource Instance { get; } = new();

    public string this[string key] => Loc.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyAllChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
