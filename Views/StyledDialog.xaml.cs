using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Wpf.Ui.Controls;

namespace DriveFlip.Views;

public partial class StyledDialog : Window
{
    public bool Confirmed { get; private set; }
    private static Rectangle? _overlay;

    private StyledDialog()
    {
        InitializeComponent();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private static void ShowOverlay(Window owner)
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

    private static void HideOverlay(Window owner)
    {
        if (_overlay == null || owner?.Content is not Grid ownerGrid) return;
        ownerGrid.Children.Remove(_overlay);
        _overlay = null;
    }

    private static StyledDialog Create(string title, string message, Brush titleColor,
        bool showYesNo)
    {
        var dlg = new StyledDialog();
        dlg.TitleText.Text = title;
        dlg.TitleText.Foreground = titleColor;
        dlg.AccentBar.Background = titleColor;
        dlg.MessageText.Text = message;

        var owner = Application.Current.MainWindow;
        if (owner != null && owner.IsLoaded)
            dlg.Owner = owner;

        if (showYesNo)
        {
            var noBtn = CreateButton("No", false);
            noBtn.Click += (_, _) => { dlg.Confirmed = false; dlg.Close(); };
            noBtn.Margin = new Thickness(0, 0, 8, 0);

            var yesBtn = CreateButton("Yes", true);
            yesBtn.Click += (_, _) => { dlg.Confirmed = true; dlg.Close(); };

            dlg.ButtonPanel.Children.Add(noBtn);
            dlg.ButtonPanel.Children.Add(yesBtn);
        }
        else
        {
            var okBtn = CreateButton("OK", true);
            okBtn.Click += (_, _) => dlg.Close();
            dlg.ButtonPanel.Children.Add(okBtn);
        }

        return dlg;
    }

    private static Wpf.Ui.Controls.Button CreateButton(string text, bool isPrimary)
    {
        var btn = new Wpf.Ui.Controls.Button
        {
            Content = text,
            Padding = new Thickness(24, 8, 24, 8),
            FontSize = 13,
            Cursor = Cursors.Hand,
            Appearance = isPrimary
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary
        };
        return btn;
    }

    private static bool ShowModal(StyledDialog dlg)
    {
        var owner = dlg.Owner;
        ShowOverlay(owner);
        dlg.ShowDialog();
        HideOverlay(owner);
        return dlg.Confirmed;
    }

    public static void ShowInfo(string title, string message)
    {
        var accent = (Brush)Application.Current.FindResource("AccentBlueBrush");
        var dlg = Create(title, message, accent, false);
        ShowModal(dlg);
    }

    public static bool ShowQuestion(string title, string message)
    {
        var accent = (Brush)Application.Current.FindResource("AccentBlueBrush");
        var dlg = Create(title, message, accent, true);
        return ShowModal(dlg);
    }

    public static bool ShowWarning(string title, string message)
    {
        var accent = (Brush)Application.Current.FindResource("AccentAmberBrush");
        var dlg = Create(title, message, accent, true);
        return ShowModal(dlg);
    }

    public static bool ShowDanger(string title, string message)
    {
        var accent = (Brush)Application.Current.FindResource("AccentRedBrush");
        var dlg = Create(title, message, accent, true);
        return ShowModal(dlg);
    }
}
