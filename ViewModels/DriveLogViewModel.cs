using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using DriveFlip.Localization;
using DriveFlip.Models;
using DriveFlip.Services;
using Microsoft.Win32;

namespace DriveFlip.ViewModels;

[SupportedOSPlatform("windows")]
public class DriveLogViewModel : INotifyPropertyChanged
{
    private readonly DriveLogService _service;

    public ObservableCollection<DriveLogEntry> FilteredEntries { get; } = new();

    // ── Selection ──
    private DriveLogEntry? _selectedEntry;
    public DriveLogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set { _selectedEntry = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelection)); }
    }

    public bool HasSelection => SelectedEntry != null;

    // ── Filtering ──
    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); ApplyFilter(); }
    }

    private DriveLogStatus? _statusFilter;
    public DriveLogStatus? StatusFilter
    {
        get => _statusFilter;
        set { _statusFilter = value; OnPropertyChanged(); ApplyFilter(); }
    }

    // ── New Note Input ──
    private string _newNoteText = "";
    public string NewNoteText
    {
        get => _newNoteText;
        set { _newNoteText = value; OnPropertyChanged(); }
    }

    // ── Drive Count ──
    public string DriveCountText => Loc.Format("DriveLogCount", FilteredEntries.Count);

    // ── Status Options for ComboBox ──
    public DriveLogStatus[] StatusOptions { get; } = Enum.GetValues<DriveLogStatus>();

    // ── Commands ──
    public ICommand SetStatusFilterCommand { get; }
    public ICommand ClearStatusFilterCommand { get; }
    public ICommand ChangeStatusCommand { get; }
    public ICommand AddNoteCommand { get; }
    public ICommand RemoveNoteCommand { get; }
    public ICommand AddPhotoCommand { get; }
    public ICommand RemovePhotoCommand { get; }
    public ICommand OpenPhotoCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand DeleteEntryCommand { get; }

    public DriveLogViewModel(DriveLogService service)
    {
        _service = service;

        SetStatusFilterCommand = new RelayCommand(p =>
        {
            if (p is DriveLogStatus status) StatusFilter = status;
        });
        ClearStatusFilterCommand = new RelayCommand(() => StatusFilter = null);
        ChangeStatusCommand = new RelayCommand(p =>
        {
            if (SelectedEntry != null && p is DriveLogStatus newStatus)
            {
                _service.UpdateStatus(SelectedEntry.SerialNumber, newStatus);
                SelectedEntry.Status = newStatus;
                OnPropertyChanged(nameof(SelectedEntry));
                ApplyFilter();
            }
        });
        AddNoteCommand = new RelayCommand(() =>
        {
            if (SelectedEntry == null || string.IsNullOrWhiteSpace(NewNoteText)) return;
            _service.AddNote(SelectedEntry.SerialNumber, NewNoteText.Trim());
            NewNoteText = "";
            RefreshSelectedEntry();
        });
        RemoveNoteCommand = new RelayCommand(p =>
        {
            if (SelectedEntry != null && p is LogNote note)
            {
                _service.RemoveNote(SelectedEntry.SerialNumber, note);
                RefreshSelectedEntry();
            }
        });
        AddPhotoCommand = new RelayCommand(() =>
        {
            if (SelectedEntry == null) return;
            var dlg = new OpenFileDialog
            {
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                    _service.AddPhoto(SelectedEntry.SerialNumber, file);
                RefreshSelectedEntry();
            }
        });
        RemovePhotoCommand = new RelayCommand(p =>
        {
            if (SelectedEntry != null && p is string fileName)
            {
                _service.RemovePhoto(SelectedEntry.SerialNumber, fileName);
                RefreshSelectedEntry();
            }
        });
        OpenPhotoCommand = new RelayCommand(p =>
        {
            if (SelectedEntry != null && p is string fileName)
            {
                var path = _service.GetPhotoFullPath(SelectedEntry.SerialNumber, fileName);
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (Exception ex) { Logger.Warning($"Failed to open photo: {ex.Message}"); }
            }
        });
        ExportCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFolderDialog
            {
                Title = Loc.Get("DriveLogExportFolder")
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _service.Export(dlg.FolderName);
                    Views.StyledDialog.ShowInfo(Loc.Get("DriveLogExportTitle"),
                        Loc.Format("DriveLogExportSuccess", dlg.FolderName));
                }
                catch (Exception ex)
                {
                    Views.StyledDialog.ShowWarning(Loc.Get("DriveLogExportTitle"),
                        Loc.Format("StatusError", ex.Message));
                }
            }
        });
        DeleteEntryCommand = new RelayCommand(() =>
        {
            if (SelectedEntry == null) return;
            var confirmed = Views.StyledDialog.ShowQuestion(
                Loc.Get("DriveLogDeleteTitle"),
                Loc.Format("DriveLogDeleteConfirm", SelectedEntry.Model, SelectedEntry.SerialNumber));
            if (confirmed)
            {
                _service.DeleteEntry(SelectedEntry.SerialNumber);
                SelectedEntry = null;
                Refresh();
            }
        });
    }

    public void Refresh()
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var all = _service.Entries;
        var filtered = all.AsEnumerable();

        if (_statusFilter.HasValue)
            filtered = filtered.Where(e => e.Status == _statusFilter.Value);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var query = _searchText.Trim();
            filtered = filtered.Where(e =>
                e.SerialNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Model.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Manufacturer.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Notes.Any(n => n.Text.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var list = filtered.OrderByDescending(e => e.LastSeenUtc).ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            // Preserve selection
            var selectedSerial = SelectedEntry?.SerialNumber;

            FilteredEntries.Clear();
            foreach (var e in list)
                FilteredEntries.Add(e);

            OnPropertyChanged(nameof(DriveCountText));

            if (selectedSerial != null)
                SelectedEntry = FilteredEntries.FirstOrDefault(e =>
                    string.Equals(e.SerialNumber, selectedSerial, StringComparison.OrdinalIgnoreCase));
        });
    }

    private void RefreshSelectedEntry()
    {
        if (SelectedEntry == null) return;
        var serial = SelectedEntry.SerialNumber;
        // Re-fetch from service to get updated data
        var updated = _service.GetEntry(serial);
        if (updated != null)
        {
            // Force UI refresh by nulling then reassigning
            SelectedEntry = null;
            SelectedEntry = updated;
        }
        ApplyFilter();
    }

    public string GetPhotoFullPath(string fileName)
    {
        if (SelectedEntry == null) return "";
        return _service.GetPhotoFullPath(SelectedEntry.SerialNumber, fileName);
    }

    public void AddPhotoFromPath(string filePath)
    {
        if (SelectedEntry == null) return;
        _service.AddPhoto(SelectedEntry.SerialNumber, filePath);
        RefreshSelectedEntry();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
