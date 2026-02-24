using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DriveFlip.Localization;
using DriveFlip.ViewModels;
using Wpf.Ui.Controls;

namespace DriveFlip.Views;

public partial class MainWindow : FluentWindow
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int LR_DEFAULTSIZE = 0x0040;
    private const int LR_LOADFROMFILE = 0x0010;
    private const int IMAGE_ICON = 1;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type,
        int cx, int cy, uint fuLoad);

    private static readonly char[] HexChars = "0123456789abcdefABCDEF".ToCharArray();
    private static readonly int[] HyphenPositions = [8, 13, 18, 23]; // GUID format: 8-4-4-4-12

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.IsRefreshing) && !vm.IsRefreshing)
                        Dispatcher.InvokeAsync(() => AdjustWindowHeight(vm.Drives.Count));
                };
            }
        };

        // Ctrl+Plus / Ctrl+Minus for detail pane zoom
        PreviewKeyDown += (_, e) =>
        {
            if (DataContext is not MainViewModel vm) return;
            if (Keyboard.Modifiers != ModifierKeys.Control) return;

            if (e.Key is Key.OemPlus or Key.Add)
            {
                vm.ZoomInCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key is Key.OemMinus or Key.Subtract)
            {
                vm.ZoomOutCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key is Key.D0 or Key.NumPad0)
            {
                vm.ZoomResetCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.IsScanning)
        {
            e.Cancel = true;
            StyledDialog.ShowInfo(
                Loc.Get("DialogCannotCloseTitle"),
                Loc.Get("DialogCannotCloseMessage"));
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // FluentWindow with ExtendsContentIntoTitleBar removes WS_SYSMENU,
        // which strips the Win32-level icon. Write the embedded .ico to a temp
        // file and use LoadImage so it works in both debug and release builds.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/logo-icon.ico", UriKind.Absolute);
            var info = Application.GetResourceStream(iconUri);
            if (info == null) return;

            var tempIcon = Path.Combine(Path.GetTempPath(), "DriveFlip_icon.ico");
            using (var fs = File.Create(tempIcon))
                info.Stream.CopyTo(fs);

            var hBig = LoadImage(IntPtr.Zero, tempIcon, IMAGE_ICON, 256, 256, LR_LOADFROMFILE);
            var hSmall = LoadImage(IntPtr.Zero, tempIcon, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);

            if (hBig != IntPtr.Zero)
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
            if (hSmall != IntPtr.Zero)
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
        }
        catch { }
    }

    private void AdjustWindowHeight(int driveCount)
    {
        const double chromeHeight = 340; // title bar, header, status bar, margins (no bottom info panel)
        const double driveCardHeight = 58; // compact drive cards
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
