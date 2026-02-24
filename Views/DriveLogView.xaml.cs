using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DriveFlip.ViewModels;

namespace DriveFlip.Views;

public partial class DriveLogView : UserControl
{
    private bool _isUpdatingStatus;

    public DriveLogView()
    {
        InitializeComponent();
    }

    private DriveLogViewModel? VM => DataContext as DriveLogViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (VM?.HasSelection == true && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Any(IsImageFile))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (VM?.HasSelection != true || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var file in files.Where(IsImageFile))
            VM.AddPhotoFromPath(file);
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp";
    }

    private void Photo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string fileName })
        {
            VM?.OpenPhotoCommand.Execute(fileName);
        }
    }

    private void NoteTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && VM?.AddNoteCommand.CanExecute(null) == true)
        {
            VM.AddNoteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingStatus) return;
        if (sender is ComboBox combo && combo.SelectedItem is Models.DriveLogStatus status)
        {
            _isUpdatingStatus = true;
            try { VM?.ChangeStatusCommand.Execute(status); }
            finally { _isUpdatingStatus = false; }
        }
    }
}
