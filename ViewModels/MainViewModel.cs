using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using DriveFlip.Models;
using DriveFlip.Services;
using Microsoft.Win32;

namespace DriveFlip.ViewModels;

[SupportedOSPlatform("windows")]
public class MainViewModel : INotifyPropertyChanged
{
    private readonly DiskEngine _engine = new();
    private CancellationTokenSource? _cts;

    // ── Collections ──
    public ObservableCollection<DriveItemViewModel> Drives { get; } = new();

    // ── State ──
    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set { _isScanning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsIdle => !IsScanning;

    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set { _isSettingsOpen = value; OnPropertyChanged(); }
    }

    private string _globalStatus = "Select drives and choose an action.";
    public string GlobalStatus
    {
        get => _globalStatus;
        set { _globalStatus = value; OnPropertyChanged(); }
    }

    // ── Drive Info Selection ──
    private DriveItemViewModel? _selectedDriveItem;
    public DriveItemViewModel? SelectedDriveItem
    {
        get => _selectedDriveItem;
        private set
        {
            if (_selectedDriveItem == value) return;
            if (_selectedDriveItem != null) _selectedDriveItem.IsInfoSelected = false;
            _selectedDriveItem = value;
            if (_selectedDriveItem != null) _selectedDriveItem.IsInfoSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDrive));
            OnPropertyChanged(nameof(SelectedDriveHealth));
            OnPropertyChanged(nameof(HasSelectedDrive));
            OnPropertyChanged(nameof(SmartDataQueried));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public PhysicalDrive? SelectedDrive => _selectedDriveItem?.Drive;
    public DriveHealthInfo? SelectedDriveHealth => _selectedDriveItem?.Drive.Health;
    public bool HasSelectedDrive => _selectedDriveItem != null;

    // ── SMART Data ──
    private bool _isLoadingSmartData;
    public bool IsLoadingSmartData
    {
        get => _isLoadingSmartData;
        set { _isLoadingSmartData = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public bool SmartDataQueried => SelectedDriveHealth?.SmartDataQueried == true;

    // ── External-Only Filter ──
    private bool _showExternalOnly;
    public bool ShowExternalOnly
    {
        get => _showExternalOnly;
        set
        {
            _showExternalOnly = value;
            OnPropertyChanged();
            CollectionViewSource.GetDefaultView(Drives).Refresh();
        }
    }

    // ── Wipe Options ──
    private WipeMethod _selectedWipeMethod = WipeMethod.ZeroFill;
    public WipeMethod SelectedWipeMethod
    {
        get => _selectedWipeMethod;
        set { _selectedWipeMethod = value; OnPropertyChanged(); }
    }

    private bool _verifyAfterWipe = true;
    public bool VerifyAfterWipe
    {
        get => _verifyAfterWipe;
        set { _verifyAfterWipe = value; OnPropertyChanged(); }
    }

    // ── Settings ──
    public int[] HeadTailSizeOptions { get; } = [1, 5, 10, 20];
    public int[] DurationOptions { get; } = [5, 15, 30, 60];
    public int[] PassOptions { get; } = [1, 2, 3];

    private int _headTailSizeGB = 10;
    public int HeadTailSizeGB
    {
        get => _headTailSizeGB;
        set { _headTailSizeGB = value; OnPropertyChanged(); }
    }

    private int _surfaceCheckDurationMinutes = 15;
    public int SurfaceCheckDurationMinutes
    {
        get => _surfaceCheckDurationMinutes;
        set { _surfaceCheckDurationMinutes = value; OnPropertyChanged(); }
    }

    private int _scatterDurationMinutes = 15;
    public int ScatterDurationMinutes
    {
        get => _scatterDurationMinutes;
        set { _scatterDurationMinutes = value; OnPropertyChanged(); }
    }

    private int _numberOfPasses = 1;
    public int NumberOfPasses
    {
        get => _numberOfPasses;
        set { _numberOfPasses = value; OnPropertyChanged(); }
    }

    private WipeMode _selectedWipeMode = WipeMode.SmartWipe;
    public WipeMode SelectedWipeMode
    {
        get => _selectedWipeMode;
        set { _selectedWipeMode = value; OnPropertyChanged(); }
    }

    // ── Animation ──
    public string AnimationEmoji { get; }

    // ── Commands ──
    public ICommand RefreshDrivesCommand { get; }
    public ICommand RunSurfaceCheckCommand { get; }
    public ICommand RunWipeCommand { get; }
    public ICommand RunCheckAndWipeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SaveReportCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand SelectDriveCommand { get; }
    public ICommand ToggleExternalFilterCommand { get; }
    public ICommand GetSmartDataCommand { get; }

    public MainViewModel()
    {
        // Pick a random animal at launch
        AnimationEmoji = Random.Shared.Next(2) == 0 ? "\U0001F43F" : "\U0001F436";

        RefreshDrivesCommand = new RelayCommand(RefreshDrives, () => IsIdle);
        RunSurfaceCheckCommand = new RelayCommand(async () => await RunSurfaceCheck(), () => IsIdle && HasSelection);
        RunWipeCommand = new RelayCommand(async () => await RunWipe(), () => IsIdle && HasSelection);
        RunCheckAndWipeCommand = new RelayCommand(async () => await RunCheckAndWipe(), () => IsIdle && HasSelection);
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        SaveReportCommand = new RelayCommand(SaveReport, () => Drives.Any(d => !string.IsNullOrEmpty(d.ReportText)));
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        SelectDriveCommand = new RelayCommand(SelectDrive);
        ToggleExternalFilterCommand = new RelayCommand(() => ShowExternalOnly = !ShowExternalOnly);
        GetSmartDataCommand = new RelayCommand(async () => await GetSmartData(),
            () => HasSelectedDrive && !IsLoadingSmartData && !SmartDataQueried);

        Logger.Info("DriveFlip started.");
        RefreshDrives();
    }

    private bool HasSelection => Drives.Any(d => d.IsSelected);

    private WipeSettings BuildSettings() => new()
    {
        HeadTailSizeGB = HeadTailSizeGB,
        SurfaceCheckDurationMinutes = SurfaceCheckDurationMinutes,
        ScatterDurationMinutes = ScatterDurationMinutes,
        NumberOfPasses = NumberOfPasses,
        WipeMode = SelectedWipeMode
    };

    public void RefreshDrives()
    {
        SelectedDriveItem = null;
        Drives.Clear();
        try
        {
            var detected = DriveDetectionService.DetectDrives();
            foreach (var d in detected)
            {
                var vm = new DriveItemViewModel(d);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DriveItemViewModel.IsSelected))
                        CommandManager.InvalidateRequerySuggested();
                };
                Drives.Add(vm);
            }

            // Set up collection view filter for external-only toggle
            var view = CollectionViewSource.GetDefaultView(Drives);
            view.Filter = o =>
            {
                if (!ShowExternalOnly) return true;
                return o is DriveItemViewModel vm && vm.Drive.IsRemovable;
            };

            GlobalStatus = $"Found {detected.Count} physical drive(s). Select drives and choose an action.";
        }
        catch (Exception ex)
        {
            GlobalStatus = $"Error detecting drives: {ex.Message}";
            Logger.Error("Drive refresh failed", ex);
        }
    }

    private System.Collections.Generic.List<DriveItemViewModel> GetSelectedNonSystem(out bool hadSystemDrives)
    {
        var selected = Drives.Where(d => d.IsSelected && !d.Drive.IsSystemDrive).ToList();
        var systemDrives = Drives.Where(d => d.IsSelected && d.Drive.IsSystemDrive).ToList();
        hadSystemDrives = systemDrives.Any();

        if (hadSystemDrives)
        {
            var msg = "The following system drive(s) were excluded for safety:\n\n" +
                string.Join("\n", systemDrives.Select(d => $"  - {d.Drive.DisplayName}"));
            Views.StyledDialog.ShowInfo("System Drive Protected", msg);
            foreach (var s in systemDrives) s.IsSelected = false;
        }

        return selected;
    }

    private async Task RunSurfaceCheck()
    {
        var selected = GetSelectedNonSystem(out _);
        if (!selected.Any())
        {
            GlobalStatus = "No non-system drives selected.";
            return;
        }

        var confirmed = Views.StyledDialog.ShowQuestion(
            "Confirm Surface Check",
            $"Run a {SurfaceCheckDurationMinutes}-minute surface check on {selected.Count} drive(s)?\n\n" +
            string.Join("\n", selected.Select(d => $"  - {d.Drive.DisplayName}")) +
            "\n\nThis is read-only and will not modify any data.");

        if (!confirmed) return;

        Logger.Info($"Surface check requested on {selected.Count} drive(s), duration={SurfaceCheckDurationMinutes}min");
        IsScanning = true;
        _cts = new CancellationTokenSource();
        GlobalStatus = "Running surface check...";

        foreach (var d in selected) { d.Reset(); d.IsRunning = true; }

        await RunSurfaceCheckOnDrives(selected);

        IsScanning = false;
        GlobalStatus = "Surface check complete.";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RunSurfaceCheckOnDrives(System.Collections.Generic.List<DriveItemViewModel> selected)
    {
        var tasks = selected.Select(async driveVm =>
        {
            var progress = new Progress<OperationProgress>(p =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.Progress = p.PercentComplete;
                    driveVm.StatusText = p.StatusMessage;
                    driveVm.SpeedText = $"{p.SpeedMBps:F1} MB/s";
                    driveVm.ErrorCount = p.ErrorCount;
                    driveVm.DataPresencePercent = p.TotalSectorsToProcess > 0
                        ? (double)p.DataSectorsFound / Math.Max(1, p.SectorsProcessed) * 100 : 0;
                    driveVm.TimeRemainingText = p.EstimatedRemaining.TotalSeconds > 0
                        ? $"{p.EstimatedRemaining:mm\\:ss} remaining" : "";
                    driveVm.IsComplete = p.IsComplete;
                });
            });

            try
            {
                var report = await _engine.RunSurfaceCheckAsync(
                    driveVm.Drive, SurfaceCheckDurationMinutes, progress, _cts!.Token);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.IsRunning = false;
                    driveVm.IsComplete = true;
                    driveVm.Passed = report.Passed;
                    driveVm.ReportText = ReportService.GenerateSurfaceCheckReport(report);
                    driveVm.StatusText = report.Passed ? "Healthy" : "Errors found";
                });
            }
            catch (OperationCanceledException)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.IsRunning = false;
                    driveVm.StatusText = "Cancelled";
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Surface check failed on Disk {driveVm.Drive.DeviceNumber}", ex);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.IsRunning = false;
                    driveVm.IsComplete = true;
                    driveVm.Passed = false;
                    driveVm.StatusText = ex is UnauthorizedAccessException
                        ? "Access denied \u2014 run as Administrator"
                        : $"Error: {ex.Message}";
                });
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task RunWipe()
    {
        var selected = GetSelectedNonSystem(out _);
        if (!selected.Any())
        {
            GlobalStatus = "No non-system drives selected.";
            return;
        }

        var modeName = SelectedWipeMode == WipeMode.SmartWipe ? "Smart Wipe" : "Full Wipe";
        var methodName = SelectedWipeMethod switch
        {
            WipeMethod.ZeroFill => "Zero Fill",
            WipeMethod.RandomFill => "Random Fill",
            WipeMethod.RandomThenZero => "Random + Zero",
            _ => "Unknown"
        };

        var verifyText = VerifyAfterWipe ? "\nVerification pass will run after wiping." : "";

        var firstConfirmed = Views.StyledDialog.ShowWarning(
            "Confirm Wipe",
            $"ALL DATA WILL BE PERMANENTLY DESTROYED\n\n" +
            $"Mode: {modeName}\nFill: {methodName}\nPasses: {NumberOfPasses}{verifyText}\n\n" +
            $"Drives to wipe:\n" +
            string.Join("\n", selected.Select(d =>
                $"  - {d.Drive.DisplayName} [{d.Drive.DriveLettersSummary}]")) +
            "\n\nThis action CANNOT be undone. Continue?");

        if (!firstConfirmed) return;

        var finalConfirmed = Views.StyledDialog.ShowDanger(
            "Final Confirmation",
            "FINAL WARNING\n\n" +
            "Are you absolutely sure you want to permanently erase " +
            $"{selected.Count} drive(s)?\n\n" +
            "Click YES only if you are certain.");

        if (!finalConfirmed) return;

        Logger.Info($"Wipe requested: mode={modeName}, fill={methodName}, passes={NumberOfPasses}, drives={selected.Count}");
        IsScanning = true;
        _cts = new CancellationTokenSource();
        GlobalStatus = "Wiping drives...";

        foreach (var d in selected) { d.Reset(); d.IsRunning = true; }

        await RunWipeOnDrives(selected);

        IsScanning = false;
        GlobalStatus = "Wipe operation complete.";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RunWipeOnDrives(System.Collections.Generic.List<DriveItemViewModel> selected)
    {
        var settings = BuildSettings();

        var tasks = selected.Select(async driveVm =>
        {
            var progress = new Progress<OperationProgress>(p =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.Progress = p.PercentComplete;
                    driveVm.StatusText = p.StatusMessage;
                    driveVm.SpeedText = $"{p.SpeedMBps:F1} MB/s";
                    driveVm.ErrorCount = p.ErrorCount;
                    driveVm.TimeRemainingText = p.EstimatedRemaining.TotalSeconds > 0
                        ? $"{p.EstimatedRemaining:hh\\:mm\\:ss} remaining" : "";
                    driveVm.IsComplete = p.IsComplete;
                });
            });

            try
            {
                WipeReport report;
                if (SelectedWipeMode == WipeMode.SmartWipe)
                {
                    report = await _engine.RunSmartWipeAsync(
                        driveVm.Drive, settings, SelectedWipeMethod, VerifyAfterWipe, progress, _cts!.Token);
                }
                else
                {
                    report = await _engine.RunFullWipeAsync(
                        driveVm.Drive, SelectedWipeMethod, settings.NumberOfPasses, VerifyAfterWipe, progress, _cts!.Token);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.IsRunning = false;
                    driveVm.IsComplete = true;
                    driveVm.Passed = report.Completed && report.WriteErrors == 0;
                    driveVm.ReportText = ReportService.GenerateWipeReport(report);
                    driveVm.StatusText = report.Completed ? "Wipe complete" : "Wipe incomplete";
                });
            }
            catch (OperationCanceledException)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.IsRunning = false;
                    driveVm.StatusText = "Cancelled";
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Wipe failed on Disk {driveVm.Drive.DeviceNumber}", ex);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.IsRunning = false;
                    driveVm.IsComplete = true;
                    driveVm.Passed = false;
                    driveVm.StatusText = ex is UnauthorizedAccessException
                        ? "Access denied \u2014 run as Administrator"
                        : $"Error: {ex.Message}";
                });
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task RunCheckAndWipe()
    {
        var selected = GetSelectedNonSystem(out _);
        if (!selected.Any())
        {
            GlobalStatus = "No non-system drives selected.";
            return;
        }

        var modeName = SelectedWipeMode == WipeMode.SmartWipe ? "Smart Wipe" : "Full Wipe";

        var confirmed = Views.StyledDialog.ShowQuestion(
            "Confirm Check & Wipe",
            $"CHECK & WIPE \u2014 Two-phase operation\n\n" +
            $"1. Surface check ({SurfaceCheckDurationMinutes} min per drive)\n" +
            $"2. {modeName} (if check passes)\n\n" +
            $"Drives:\n" +
            string.Join("\n", selected.Select(d => $"  - {d.Drive.DisplayName}")) +
            "\n\nIf the surface check finds errors, you'll be asked whether to continue.\n\nProceed?");

        if (!confirmed) return;

        Logger.Info($"Check & Wipe requested on {selected.Count} drive(s)");
        IsScanning = true;
        _cts = new CancellationTokenSource();

        // ── Phase 1: Surface Check ──
        GlobalStatus = "Phase 1: Running surface check...";
        foreach (var d in selected) { d.Reset(); d.IsRunning = true; }

        await RunSurfaceCheckOnDrives(selected);

        if (_cts.IsCancellationRequested)
        {
            IsScanning = false;
            GlobalStatus = "Check & Wipe cancelled.";
            return;
        }

        // ── Evaluate results ──
        var failed = selected.Where(d => !d.Passed).ToList();
        if (failed.Any())
        {
            var continueWipe = Views.StyledDialog.ShowWarning(
                "Issues Found \u2014 Continue Wipe?",
                $"Surface check found issues on {failed.Count} drive(s):\n\n" +
                string.Join("\n", failed.Select(d => $"  - {d.Drive.DisplayName}: {d.StatusText}")) +
                "\n\nDo you still want to proceed with wiping ALL selected drives?");

            if (!continueWipe)
            {
                IsScanning = false;
                GlobalStatus = "Check & Wipe stopped after surface check. Review results above.";
                CommandManager.InvalidateRequerySuggested();
                return;
            }
        }

        // ── Phase 2: Wipe ──
        GlobalStatus = "Phase 2: Wiping drives...";
        foreach (var d in selected) { d.Reset(); d.IsRunning = true; }

        await RunWipeOnDrives(selected);

        IsScanning = false;
        GlobalStatus = "Check & Wipe complete.";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task GetSmartData()
    {
        var drive = SelectedDriveItem?.Drive;
        if (drive?.Health == null) return;

        IsLoadingSmartData = true;
        try
        {
            await Task.Run(() =>
                DriveDetectionService.QueryDetailedSmartData(drive.DeviceNumber, drive.Health, drive.Health.BusType));

            Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(SelectedDriveHealth));
                OnPropertyChanged(nameof(SmartDataQueried));
                CommandManager.InvalidateRequerySuggested();
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"SMART data query failed for disk {drive.DeviceNumber}", ex);
        }
        finally
        {
            IsLoadingSmartData = false;
        }
    }

    private void Cancel()
    {
        _cts?.Cancel();
        GlobalStatus = "Cancelling...";
        Logger.Info("Operation cancelled by user.");
    }

    private void SaveReport()
    {
        var allReports = string.Join("\n\n",
            Drives.Where(d => !string.IsNullOrEmpty(d.ReportText))
                  .Select(d => d.ReportText));

        if (string.IsNullOrWhiteSpace(allReports)) return;

        var dlg = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = $"DriveFlip_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Title = "Save Report"
        };

        if (dlg.ShowDialog() == true)
        {
            ReportService.SaveReport(allReports, dlg.FileName);
            GlobalStatus = $"Report saved to {dlg.FileName}";
            Logger.Info($"Report saved: {dlg.FileName}");
        }
    }

    private void SelectDrive(object? param)
    {
        var driveVm = param as DriveItemViewModel;
        if (driveVm == null) return;

        // Toggle: clicking same drive deselects
        if (SelectedDriveItem == driveVm)
        {
            SelectedDriveItem = null;
            return;
        }

        SelectedDriveItem = driveVm;

        // Lazy-load health data on background thread
        if (driveVm.Drive.Health == null)
        {
            Task.Run(() =>
            {
                var health = DriveDetectionService.QueryHealthInfo(driveVm.Drive.DeviceNumber);
                driveVm.Drive.Health = health;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(SelectedDriveHealth));
                });
            });
        }
    }

    private void SelectAll()
    {
        foreach (var d in Drives)
            if (!d.Drive.IsSystemDrive && (!ShowExternalOnly || d.Drive.IsRemovable))
                d.IsSelected = true;
    }

    private void DeselectAll()
    {
        foreach (var d in Drives)
            d.IsSelected = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
