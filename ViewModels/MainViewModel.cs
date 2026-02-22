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
using LicenseStatusEnum = DriveFlip.Models.LicenseStatus;

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

    // ── Operations Dashboard (aggregated from all running drives) ──
    private double _aggregateSpeedMBps;
    public double AggregateSpeedMBps
    {
        get => _aggregateSpeedMBps;
        set { _aggregateSpeedMBps = value; OnPropertyChanged(); OnPropertyChanged(nameof(AggregateSpeedDisplay)); OnPropertyChanged(nameof(SpeedGaugePercent)); }
    }

    public string AggregateSpeedDisplay => $"{_aggregateSpeedMBps:F1}";

    // Gauge as percent of a reasonable max (500 MB/s for SATA SSD ceiling)
    private const double MaxExpectedMBps = 500.0;
    public double SpeedGaugePercent => Math.Min(100, _aggregateSpeedMBps / MaxExpectedMBps * 100);



    private string _operationSummary = "";
    public string OperationSummary
    {
        get => _operationSummary;
        set { _operationSummary = value; OnPropertyChanged(); }
    }

    private double _aggregateProgress;
    public double AggregateProgress
    {
        get => _aggregateProgress;
        set { _aggregateProgress = value; OnPropertyChanged(); }
    }

    // ── Drive Info Selection ──
    private DriveItemViewModel? _selectedDriveItem;
    public DriveItemViewModel? SelectedDriveItem
    {
        get => _selectedDriveItem;
        set
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

    private int _selectedInfoTab;
    public int SelectedInfoTab
    {
        get => _selectedInfoTab;
        set { _selectedInfoTab = value; OnPropertyChanged(); }
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

    // ── License State ──
    private LicenseStatusEnum _licenseStatus = LicenseStatusEnum.Unknown;
    public LicenseStatusEnum LicenseStatusValue
    {
        get => _licenseStatus;
        set
        {
            _licenseStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLicensed));
            OnPropertyChanged(nameof(LicenseBannerVisible));
            OnPropertyChanged(nameof(LicenseStatusText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private LicensePayload? _licensePayload;
    public LicensePayload? LicensePayloadValue
    {
        get => _licensePayload;
        set
        {
            _licensePayload = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LicenseeDisplay));
            OnPropertyChanged(nameof(LicenseEditionDisplay));
            OnPropertyChanged(nameof(LicenseExpiresDisplay));
        }
    }

    public bool IsLicensed => _licenseStatus is LicenseStatusEnum.Valid or LicenseStatusEnum.CachedOffline;
    public bool LicenseBannerVisible => !IsLicensed;

    public string LicenseStatusText => _licenseStatus switch
    {
        LicenseStatusEnum.Valid => "Licensed",
        LicenseStatusEnum.CachedOffline => "Licensed (offline)",
        LicenseStatusEnum.Expired => "License expired",
        LicenseStatusEnum.Invalid => "Invalid license",
        _ => "Unlicensed"
    };

    public string LicenseeDisplay => _licensePayload?.Licensee ?? "";
    public string LicenseEditionDisplay => _licensePayload?.Edition ?? "";
    public string LicenseExpiresDisplay => _licensePayload != null
        ? _licensePayload.ExpiresUtc.ToLocalTime().ToString("yyyy-MM-dd") : "";

    private string _licenseKeyInput = "";
    public string LicenseKeyInput
    {
        get => _licenseKeyInput;
        set
        {
            _licenseKeyInput = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLicenseKeyValid));
            // Clear error when user edits
            if (!string.IsNullOrEmpty(LicenseErrorMessage))
                LicenseErrorMessage = "";
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsLicenseKeyValid => LicenseService.IsWellFormedLicenseKey(_licenseKeyInput?.Trim() ?? "");

    private string _licenseErrorMessage = "";
    public string LicenseErrorMessage
    {
        get => _licenseErrorMessage;
        set { _licenseErrorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLicenseError)); }
    }

    public bool HasLicenseError => !string.IsNullOrEmpty(_licenseErrorMessage);

    // ── Activation rate limiter ──
    private const int MaxActivationAttempts = 5;
    private static readonly TimeSpan ActivationLockoutDuration = TimeSpan.FromMinutes(10);
    private readonly System.Collections.Generic.HashSet<string> _attemptedKeys = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _lockoutUntil;

    private bool IsActivationLockedOut => _lockoutUntil.HasValue && DateTime.UtcNow < _lockoutUntil.Value;

    private bool CanActivate => IsLicenseKeyValid && !IsActivatingLicense && !IsActivationLockedOut;

    private bool _isActivatingLicense;
    public bool IsActivatingLicense
    {
        get => _isActivatingLicense;
        set { _isActivatingLicense = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private bool _isLicenseSettingsOpen;
    public bool IsLicenseSettingsOpen
    {
        get => _isLicenseSettingsOpen;
        set { _isLicenseSettingsOpen = value; OnPropertyChanged(); }
    }

    // ── Filters ──
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

    // ── Drive Protection ──
    private bool _protectSystemDrives = true;
    public bool ProtectSystemDrives
    {
        get => _protectSystemDrives;
        set { _protectSystemDrives = value; OnPropertyChanged(); UpdateDriveProtection(); }
    }

    private bool _protectInternalDrives = true;
    public bool ProtectInternalDrives
    {
        get => _protectInternalDrives;
        set { _protectInternalDrives = value; OnPropertyChanged(); UpdateDriveProtection(); }
    }

    private void UpdateDriveProtection()
    {
        foreach (var driveVm in Drives)
        {
            driveVm.IsProtected =
                (ProtectSystemDrives && driveVm.Drive.IsSystemDrive) ||
                (ProtectInternalDrives && !driveVm.Drive.IsRemovable);

            if (driveVm.IsProtected && driveVm.IsSelected)
                driveVm.IsSelected = false;
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
    public ICommand ActivateLicenseCommand { get; }
    public ICommand RevalidateLicenseCommand { get; }
    public ICommand ToggleLicenseSettingsCommand { get; }

    public MainViewModel()
    {
        RefreshDrivesCommand = new RelayCommand(async () => await SafeAsync(RefreshDrivesAsync), () => IsIdle);
        RunSurfaceCheckCommand = new RelayCommand(async () => await SafeAsync(RunSurfaceCheck), () => IsIdle && HasSelection && IsLicensed);
        RunWipeCommand = new RelayCommand(async () => await SafeAsync(RunWipe), () => IsIdle && HasSelection && IsLicensed);
        RunCheckAndWipeCommand = new RelayCommand(async () => await SafeAsync(RunCheckAndWipe), () => IsIdle && HasSelection && IsLicensed);
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        SaveReportCommand = new RelayCommand(SaveReport, () => Drives.Any(d => !string.IsNullOrEmpty(d.ReportText)));
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        SelectDriveCommand = new RelayCommand(SelectDrive);
        ToggleExternalFilterCommand = new RelayCommand(() => ShowExternalOnly = !ShowExternalOnly);
        GetSmartDataCommand = new RelayCommand(async () => await SafeAsync(GetSmartData),
            () => HasSelectedDrive && !IsLoadingSmartData && !SmartDataQueried);
        ActivateLicenseCommand = new RelayCommand(async () => await SafeAsync(ActivateLicense), () => CanActivate);
        RevalidateLicenseCommand = new RelayCommand(async () => await SafeAsync(RevalidateLicense), () => !IsActivatingLicense);
        ToggleLicenseSettingsCommand = new RelayCommand(() => IsLicenseSettingsOpen = !IsLicenseSettingsOpen);

        Logger.Info("DriveFlip started.");
        _ = RefreshDrivesAsync();
    }

    // ── License Methods ──

    public void InitializeLicense(LicenseStatusEnum status, LicensePayload? payload)
    {
        LicenseStatusValue = status;
        LicensePayloadValue = payload;

        var settings = LicenseService.LoadSettings();
        LicenseKeyInput = settings.LicenseKey;
    }

    public async Task RevalidateLicenseInBackgroundAsync()
    {
        try
        {
            var (status, payload) = await LicenseService.RevalidateAsync();
            if (status == LicenseStatusEnum.Valid)
            {
                LicenseStatusValue = status;
                LicensePayloadValue = payload;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Background license revalidation failed: {ex.Message}");
        }
    }

    private async Task ActivateLicense()
    {
        LicenseErrorMessage = "";

        if (IsActivationLockedOut)
        {
            var remaining = _lockoutUntil!.Value - DateTime.UtcNow;
            LicenseErrorMessage = $"Too many attempts. Try again in {remaining.Minutes}m {remaining.Seconds}s.";
            return;
        }

        var key = LicenseKeyInput.Trim();

        // Track distinct keys attempted
        _attemptedKeys.Add(key);
        if (_attemptedKeys.Count > MaxActivationAttempts)
        {
            _lockoutUntil = DateTime.UtcNow.Add(ActivationLockoutDuration);
            var remaining = ActivationLockoutDuration;
            LicenseErrorMessage = $"Too many attempts. Try again in {remaining.Minutes} minutes.";
            Logger.Warning($"License activation locked out until {_lockoutUntil.Value:u} after {_attemptedKeys.Count} distinct keys.");
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        var settings = LicenseService.LoadSettings();
        var endpointUrl = settings.EndpointUrl;
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            LicenseErrorMessage = "No license endpoint configured.";
            Logger.Warning("License activation attempted without endpoint URL configured.");
            return;
        }

        IsActivatingLicense = true;

        try
        {
            var (status, payload) = await LicenseService.ActivateLicenseAsync(key, endpointUrl);

            LicenseStatusValue = status;
            LicensePayloadValue = payload;

            if (IsLicensed)
            {
                LicenseErrorMessage = "";
                GlobalStatus = "License activated successfully!";
                IsLicenseSettingsOpen = false;
                // Reset rate limiter on success
                _attemptedKeys.Clear();
                _lockoutUntil = null;
            }
            else
            {
                LicenseErrorMessage = status switch
                {
                    LicenseStatusEnum.Expired => "This license key has expired.",
                    _ => "Invalid license key or signature verification failed."
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Error("License activation error", ex);
            LicenseErrorMessage = $"Activation failed: {ex.Message}";
        }
        finally
        {
            IsActivatingLicense = false;
        }
    }

    private async Task RevalidateLicense()
    {
        IsActivatingLicense = true;
        GlobalStatus = "Revalidating license...";

        try
        {
            var (status, payload) = await LicenseService.RevalidateAsync();
            LicenseStatusValue = status;
            LicensePayloadValue = payload;
            GlobalStatus = IsLicensed ? "License revalidated." : "Revalidation failed.";
        }
        catch (Exception ex)
        {
            Logger.Error("License revalidation error", ex);
            GlobalStatus = $"Revalidation error: {ex.Message}";
        }
        finally
        {
            IsActivatingLicense = false;
        }
    }

    private bool HasSelection => Drives.Any(d => d.IsSelected);

    private void UpdateDashboard()
    {
        var running = Drives.Where(d => d.IsRunning).ToList();
        var completed = Drives.Where(d => d.IsComplete && !d.IsRunning).ToList();
        var total = running.Count + completed.Count;

        AggregateSpeedMBps = running.Sum(d =>
        {
            if (double.TryParse(d.SpeedText.Replace(" MB/s", ""), out var s)) return s;
            return 0;
        });

        AggregateProgress = total > 0
            ? (running.Sum(d => d.Progress) + completed.Count * 100.0) / total
            : 0;

        OperationSummary = $"{completed.Count} of {total} drive(s) complete";
    }

    private WipeSettings BuildSettings() => new()
    {
        HeadTailSizeGB = HeadTailSizeGB,
        SurfaceCheckDurationMinutes = SurfaceCheckDurationMinutes,
        ScatterDurationMinutes = ScatterDurationMinutes,
        NumberOfPasses = NumberOfPasses,
        WipeMode = SelectedWipeMode
    };

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { _isRefreshing = value; OnPropertyChanged(); }
    }

    public async Task RefreshDrivesAsync()
    {
        SelectedDriveItem = null;
        Drives.Clear();
        IsRefreshing = true;
        GlobalStatus = "Detecting drives...";

        try
        {
            var detected = await Task.Run(() => DriveDetectionService.DetectDrives());

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

            // Set up collection view filter
            var view = CollectionViewSource.GetDefaultView(Drives);
            view.Filter = o =>
            {
                if (o is not DriveItemViewModel vm) return false;
                if (ShowExternalOnly && !vm.Drive.IsRemovable) return false;
                return true;
            };

            // Apply drive protection and compute size indicators
            UpdateDriveProtection();
            if (Drives.Count > 0)
            {
                long maxSize = Drives.Max(d => d.Drive.SizeBytes);
                foreach (var vm in Drives)
                    vm.SizePercent = maxSize > 0 ? (double)vm.Drive.SizeBytes / maxSize * 100.0 : 0;
            }

            GlobalStatus = $"Found {detected.Count} drive(s). Querying health data...";

            // Auto-query SMART/health for each drive sequentially with fault isolation
            foreach (var driveVm in Drives.ToList())
            {
                driveVm.IsQueryingHealth = true;
                try
                {
                    var devNum = driveVm.Drive.DeviceNumber;
                    var health = await Task.Run(() => DriveDetectionService.QueryHealthInfo(devNum));
                    driveVm.Drive.Health = health;

                    // Query detailed SMART data for risk assessment
                    await Task.Run(() =>
                        DriveDetectionService.QueryDetailedSmartData(devNum, health, health.BusType));

                    driveVm.RiskLevel = health.RiskLevel;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Health query failed for disk {driveVm.Drive.DeviceNumber}: {ex.Message}");
                    driveVm.Drive.Health = new DriveHealthInfo { HealthStatus = "Query failed" };
                    driveVm.RiskLevel = DriveRiskLevel.Unknown;
                }
                finally
                {
                    driveVm.IsQueryingHealth = false;
                }
            }

            // Refresh selected drive health bindings if applicable
            OnPropertyChanged(nameof(SelectedDriveHealth));
            OnPropertyChanged(nameof(SmartDataQueried));

            GlobalStatus = $"Found {detected.Count} physical drive(s). Select drives and choose an action.";
        }
        catch (Exception ex)
        {
            GlobalStatus = $"Error detecting drives: {ex.Message}";
            Logger.Error("Drive refresh failed", ex);
        }
        finally
        {
            IsRefreshing = false;
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
        SelectedDriveItem = selected[0];
        SelectedInfoTab = 2;

        await RunSurfaceCheckOnDrives(selected);

        IsScanning = false;
        GlobalStatus = "Surface check complete.";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RunSurfaceCheckOnDrives(System.Collections.Generic.List<DriveItemViewModel> selected)
    {
        // Init visualization for surface check (phases only if not already set by Check & Wipe)
        foreach (var driveVm in selected)
            Application.Current.Dispatcher.Invoke(() =>
            {
                driveVm.InitVisualization(0, 0, false, true);
                if (driveVm.Phases.Count == 0)
                    driveVm.InitPhases(false, true, false);
            });

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
                    driveVm.IsComplete = p.IsComplete;
                    driveVm.UpdateVisualization(p.PercentComplete, p.StatusMessage);
                    UpdateDashboard();
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
        SelectedDriveItem = selected[0];
        SelectedInfoTab = 2;

        await RunWipeOnDrives(selected);

        IsScanning = false;
        GlobalStatus = "Wipe operation complete.";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RunWipeOnDrives(System.Collections.Generic.List<DriveItemViewModel> selected)
    {
        var settings = BuildSettings();
        bool isSmartWipe = SelectedWipeMode == WipeMode.SmartWipe;

        // Init visualization for wipe
        foreach (var driveVm in selected)
        {
            int headSegs = 0, tailSegs = 0;
            if (isSmartWipe && driveVm.Drive.SizeBytes > 0)
            {
                long headBytes = (long)HeadTailSizeGB * 1024L * 1024L * 1024L;
                headSegs = (int)(headBytes * 200.0 / driveVm.Drive.SizeBytes);
                tailSegs = headSegs;
            }
            Application.Current.Dispatcher.Invoke(() =>
            {
                driveVm.InitVisualization(headSegs, tailSegs, isSmartWipe, false);
                if (driveVm.Phases.Count == 0)
                    driveVm.InitPhases(isSmartWipe, false, VerifyAfterWipe);
            });
        }

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
                    driveVm.IsComplete = p.IsComplete;
                    driveVm.UpdateVisualization(p.PercentComplete, p.StatusMessage);
                    UpdateDashboard();
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
        bool isSmartWipe = SelectedWipeMode == WipeMode.SmartWipe;
        foreach (var d in selected) { d.Reset(); d.IsRunning = true; }
        foreach (var d in selected)
            d.InitPhases(isSmartWipe, false, VerifyAfterWipe, isCheckAndWipe: true);
        SelectedDriveItem = selected[0];
        SelectedInfoTab = 2;

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
        foreach (var d in selected)
        {
            d.MarkSurfaceCheckDone();
            d.IsRunning = true;
            d.Progress = 0;
            d.StatusText = "";
            d.SpeedText = "";
            d.ErrorCount = 0;
            d.IsComplete = false;
        }
        SelectedDriveItem = selected[0];
        SelectedInfoTab = 2;

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

            // DriveHealthInfo doesn't implement INPC, so WPF won't re-read bindings
            // unless the DataContext reference itself changes. Nudge it.
            var health = drive.Health;
            drive.Health = null;
            OnPropertyChanged(nameof(SelectedDriveHealth));
            drive.Health = health;
            OnPropertyChanged(nameof(SelectedDriveHealth));
            OnPropertyChanged(nameof(SmartDataQueried));
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex)
        {
            Logger.Error($"SMART data query failed for disk {drive.DeviceNumber}", ex);
            GlobalStatus = $"SMART query failed for Disk {drive.DeviceNumber}: {ex.Message}";
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
            _ = Task.Run(() =>
            {
                try
                {
                    var health = DriveDetectionService.QueryHealthInfo(driveVm.Drive.DeviceNumber);
                    driveVm.Drive.Health = health;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        OnPropertyChanged(nameof(SelectedDriveHealth));
                        OnPropertyChanged(nameof(SmartDataQueried));
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error($"Health query failed for disk {driveVm.Drive.DeviceNumber}", ex);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Set a minimal health object so the UI doesn't stay stuck on "Loading"
                        driveVm.Drive.Health = new DriveHealthInfo { HealthStatus = "Query failed" };
                        OnPropertyChanged(nameof(SelectedDriveHealth));
                        OnPropertyChanged(nameof(SmartDataQueried));
                    });
                }
            });
        }
    }

    private void SelectAll()
    {
        foreach (var d in Drives)
            if (d.IsSelectable && (!ShowExternalOnly || d.Drive.IsRemovable))
                d.IsSelected = true;
    }

    private void DeselectAll()
    {
        foreach (var d in Drives)
            d.IsSelected = false;
    }

    /// <summary>
    /// Wraps an async Task method so that exceptions don't crash the process
    /// when called from async void (RelayCommand takes Action, not Func&lt;Task&gt;).
    /// </summary>
    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logger.Error("Unhandled error in async command", ex);
            GlobalStatus = $"Error: {ex.Message}";
            IsScanning = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
