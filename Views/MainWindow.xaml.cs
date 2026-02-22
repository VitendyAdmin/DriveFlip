using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DriveFlip.ViewModels;
using Wpf.Ui.Controls;

namespace DriveFlip.Views;

public partial class MainWindow : FluentWindow
{
    private static readonly char[] HexChars = "0123456789abcdefABCDEF".ToCharArray();
    private static readonly int[] HyphenPositions = [8, 13, 18, 23]; // GUID format: 8-4-4-4-12

    public MainWindow()
    {
        InitializeComponent();

        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsRefreshing) && !vm.IsRefreshing)
                    Dispatcher.InvokeAsync(() => AdjustWindowHeight(vm.Drives.Count));
            };
        }
    }

    private void AdjustWindowHeight(int driveCount)
    {
        const double chromeHeight = 420; // title bar, header, drive list header, info panel, status bar, margins
        const double driveCardHeight = 66;
        const int maxVisibleDrives = 4;

        int visibleDrives = Math.Max(1, Math.Min(driveCount, maxVisibleDrives));
        double targetHeight = chromeHeight + visibleDrives * driveCardHeight;

        double maxHeight = SystemParameters.WorkArea.Height * 0.6;

        Height = Math.Max(MinHeight, Math.Min(targetHeight, maxHeight));
    }

    private void LicenseBadge_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsLicenseSettingsOpen = !vm.IsLicenseSettingsOpen;
    }

    private void LicenseOverlayBackdrop_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsLicenseSettingsOpen = false;
    }

    private void LicenseKey_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb)
        {
            e.Handled = true;
            return;
        }

        // Only allow hex characters (hyphens are auto-inserted)
        if (!e.Text.All(c => HexChars.Contains(c)))
        {
            e.Handled = true;
            return;
        }

        // Auto-insert hyphen before the typed character if we're at a hyphen position
        var caretIndex = tb.CaretIndex;
        var currentText = tb.Text;

        // Remove any selected text first for accurate position calculation
        if (tb.SelectionLength > 0)
        {
            currentText = currentText.Remove(tb.SelectionStart, tb.SelectionLength);
            caretIndex = tb.SelectionStart;
        }

        if (currentText.Length < 36 && HyphenPositions.Contains(caretIndex) &&
            (caretIndex >= currentText.Length || currentText[caretIndex] != '-'))
        {
            e.Handled = true;
            tb.Text = currentText.Insert(caretIndex, "-" + e.Text);
            tb.CaretIndex = caretIndex + 2;

            // Check if another hyphen is needed right after
            AutoInsertTrailingHyphen(tb);
            return;
        }

        // After the character is added, check if we need a trailing hyphen
        tb.Dispatcher.InvokeAsync(() => AutoInsertTrailingHyphen(tb));
    }

    private static void AutoInsertTrailingHyphen(System.Windows.Controls.TextBox tb)
    {
        var pos = tb.CaretIndex;
        var text = tb.Text;
        if (text.Length < 36 && HyphenPositions.Contains(pos) &&
            (pos >= text.Length || text[pos] != '-'))
        {
            tb.Text = text.Insert(pos, "-");
            tb.CaretIndex = pos + 1;
        }
    }

    private void LicenseKey_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }

        var text = ((string)e.DataObject.GetData(typeof(string))!).Trim();

        // If pasting a raw hex string (32 chars), format it as a GUID
        var hexOnly = new string(text.Where(c => HexChars.Contains(c)).ToArray());
        if (hexOnly.Length == 32)
        {
            text = $"{hexOnly[..8]}-{hexOnly[8..12]}-{hexOnly[12..16]}-{hexOnly[16..20]}-{hexOnly[20..32]}";
        }

        // Allow pasting valid GUID-formatted strings
        var allowedChars = "0123456789abcdefABCDEF-".ToCharArray();
        if (!text.All(c => allowedChars.Contains(c)))
        {
            e.CancelCommand();
            return;
        }

        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.UnicodeText, text);
        e.DataObject = dataObject;
    }
}
