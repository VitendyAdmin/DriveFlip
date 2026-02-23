using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DriveFlip.Models;

namespace DriveFlip.Views;

public partial class ListingExportDialog : Window
{
    private readonly PhysicalDrive _drive;
    private readonly ObservableCollection<ListingField> _fields;
    public bool Copied { get; private set; }

    private static Rectangle? _overlay;

    private ListingExportDialog(PhysicalDrive drive)
    {
        InitializeComponent();
        _drive = drive;
        _fields = new ObservableCollection<ListingField>(ListingField.CreateDefaultFields());
        FieldList.ItemsSource = _fields;

        foreach (var field in _fields)
            field.PropertyChanged += (_, _) => RefreshPreview();

        RefreshPreview();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private string BuildText()
    {
        var included = _fields
            .Where(f => f.IsIncluded)
            .Select(f => new { f.Label, Value = f.GetValue(_drive) })
            .Where(x => !string.IsNullOrEmpty(x.Value))
            .ToList();

        if (included.Count == 0)
            return "";

        int maxLabel = included.Max(x => x.Label.Length);
        var sb = new StringBuilder();
        var bar = new string('\u2550', 38);

        sb.AppendLine(bar);
        sb.AppendLine("  DRIVEFLIP DRIVE REPORT");
        sb.AppendLine(bar);

        foreach (var item in included)
        {
            var padded = (item.Label + ":").PadRight(maxLabel + 1);
            sb.AppendLine($"  {padded}  {item.Value}");
        }

        sb.AppendLine(bar);
        sb.AppendLine($"  Tested with DriveFlip \u00b7 {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine(bar);

        return sb.ToString();
    }

    private void RefreshPreview()
    {
        PreviewBox.Text = BuildText();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        int idx = FieldList.SelectedIndex;
        if (idx <= 0) return;
        _fields.Move(idx, idx - 1);
        FieldList.SelectedIndex = idx - 1;
        RefreshPreview();
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        int idx = FieldList.SelectedIndex;
        if (idx < 0 || idx >= _fields.Count - 1) return;
        _fields.Move(idx, idx + 1);
        FieldList.SelectedIndex = idx + 1;
        RefreshPreview();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Copied = false;
        Close();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = BuildText();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
        Copied = true;
        Close();
    }

    // ── Overlay helpers (same pattern as StyledDialog) ──

    private static void ShowOverlay(Window? owner)
    {
        if (owner?.Content is not Grid ownerGrid) return;
        _overlay = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(_overlay, 100);
        Grid.SetColumnSpan(_overlay, 100);
        Panel.SetZIndex(_overlay, 9999);
        ownerGrid.Children.Add(_overlay);
    }

    private static void HideOverlay(Window? owner)
    {
        if (_overlay == null || owner?.Content is not Grid ownerGrid) return;
        ownerGrid.Children.Remove(_overlay);
        _overlay = null;
    }

    /// <summary>
    /// Shows the export dialog. Returns true if the user clicked Copy.
    /// </summary>
    public static bool ShowExport(PhysicalDrive drive)
    {
        var dlg = new ListingExportDialog(drive);

        var owner = Application.Current.MainWindow;
        if (owner != null && owner.IsLoaded)
            dlg.Owner = owner;

        ShowOverlay(dlg.Owner);
        dlg.ShowDialog();
        HideOverlay(dlg.Owner);

        return dlg.Copied;
    }
}
