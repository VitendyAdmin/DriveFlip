using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace DriveFlip.Localization;

/// <summary>
/// XAML markup extension for localized strings.
/// Usage: Text="{loc:Loc Key=DriveInfo}"
/// or:    Text="{loc:Loc DriveInfo}"
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationSource.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
