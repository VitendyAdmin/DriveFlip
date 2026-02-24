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
using DriveFlip.Constants;
using DriveFlip.Localization;
using DriveFlip.Models;
using DriveFlip.Services;
using Microsoft.Win32;
using LicenseStatusEnum = DriveFlip.Models.LicenseStatus;

namespace DriveFlip.ViewModels;

[SupportedOSPlatform("windows")]
public class MainViewModel : INotifyPropertyChanged
{
    private readonly DiskEngine _engine;
    private readonly DriveDetectionService _detection;
    private readonly ILicenseService _license;
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
        set { _isSettingsOpen = value; OnPropertyChanged(); if (value) IsAboutOpen = false; }
    }

    private bool _isAboutOpen;
    public bool IsAboutOpen
    {
        get => _isAboutOpen;
        set { _isAboutOpen = value; OnPropertyChanged(); if (value) IsSettingsOpen = false; }
    }

    public string AppVersion => System.Reflection.Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString(3) ?? "2.0.0";

    private string _globalStatus = Loc.Get("StatusReady");
    public string GlobalStatus
    {
        get => _globalStatus;
        set { _globalStatus = value; OnPropertyChanged(); }
    }

    // ── Operations Dashboard (aggregated from all running drives) ──
    private DateTime _operationStartedAt;

    private string _aggregateTimeRemaining = "";
    public string AggregateTimeRemaining
    {
        get => _aggregateTimeRemaining;
        set { _aggregateTimeRemaining = value; OnPropertyChanged(); }
    }

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
        LicenseStatusEnum.Valid => Loc.Get("LicenseStatusLicensed"),
        LicenseStatusEnum.CachedOffline => Loc.Get("LicenseStatusCachedOffline"),
        LicenseStatusEnum.Expired => Loc.Get("LicenseStatusExpired"),
        LicenseStatusEnum.Invalid => Loc.Get("LicenseStatusInvalid"),
        _ => Loc.Get("LicenseStatusUnlicensed")
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

    public bool IsLicenseKeyValid => _license.IsWellFormedLicenseKey(_licenseKeyInput?.Trim() ?? "");

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

    // ── Advanced Settings ──
    private bool _crashReportEnabled = AppSettings.LoadCrashReportEnabled();
    public bool CrashReportEnabled
    {
        get => _crashReportEnabled;
        set { _crashReportEnabled = value; OnPropertyChanged(); AppSettings.SaveCrashReportEnabled(value); }
    }

    private bool _showRawDataButton;
    public bool ShowRawDataButton
    {
        get => _showRawDataButton;
        set { _showRawDataButton = value; OnPropertyChanged(); }
    }

    private bool _formatAfterWipe;
    public bool FormatAfterWipe
    {
        get => _formatAfterWipe;
        set
        {
            _formatAfterWipe = value;
            OnPropertyChanged();
            if (!value) AssignLetterAfterWipe = false;
        }
    }

    private bool _assignLetterAfterWipe = true;
    public bool AssignLetterAfterWipe
    {
        get => _assignLetterAfterWipe;
        set { _assignLetterAfterWipe = value; OnPropertyChanged(); }
    }

    public LogLevel[] LogLevelOptions { get; } =
        [LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal];

    private LogLevel _selectedLogLevel = LogLevel.Error;
    public LogLevel SelectedLogLevel
    {
        get => _selectedLogLevel;
        set { _selectedLogLevel = value; OnPropertyChanged(); Logger.MinimumLevel = value; }
    }

    // ── Language ──
    public System.Collections.Generic.IReadOnlyList<LanguageOption> Languages => Loc.SupportedLanguages;

    private LanguageOption _selectedLanguage = Loc.SupportedLanguages.First(l => l.Code == Loc.CurrentLanguageCode)
        ?? Loc.SupportedLanguages[0];
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value == null || value == _selectedLanguage) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            Loc.SetLanguage(value.Code);
            AppSettings.Save(value.Code);
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

    // ── Detail Pane Zoom ──
    private double _detailZoom = 1.1;
    public double DetailZoom
    {
        get => _detailZoom;
        set { _detailZoom = Math.Clamp(value, 0.8, 2.0); OnPropertyChanged(); }
    }

    // ── Commands ──
    public ICommand RefreshDrivesCommand { get; }
    public ICommand RunSurfaceCheckCommand { get; }
    public ICommand RunWipeCommand { get; }
    public ICommand RunCheckAndWipeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ToggleAboutCommand { get; }
    public ICommand SelectDriveCommand { get; }
    public ICommand ToggleExternalFilterCommand { get; }
    public ICommand GetSmartDataCommand { get; }
    public ICommand ActivateLicenseCommand { get; }
    public ICommand RevalidateLicenseCommand { get; }
    public ICommand ToggleLicenseSettingsCommand { get; }
    public ICommand DumpDriveDataCommand { get; }
    public ICommand ExportForListingCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomResetCommand { get; }

    public MainViewModel() : this(new DriveDetectionService(), new DiskEngine(), new LicenseService()) { }

    public MainViewModel(DriveDetectionService detection, DiskEngine engine, ILicenseService license)
    {
        _detection = detection;
        _engine = engine;
        _license = license;
        RefreshDrivesCommand = new RelayCommand(async () => await SafeAsync(RefreshDrivesAsync, "RefreshDrives"), () => IsIdle);
        RunSurfaceCheckCommand = new RelayCommand(async () => await SafeAsync(RunSurfaceCheck, "SurfaceCheck"), () => IsIdle && HasSelection && IsLicensed);
        RunWipeCommand = new RelayCommand(async () => await SafeAsync(RunWipe, "Wipe"), () => IsIdle && HasSelection && IsLicensed);
        RunCheckAndWipeCommand = new RelayCommand(async () => await SafeAsync(RunCheckAndWipe, "CheckAndWipe"), () => IsIdle && HasSelection && IsLicensed);
        CancelCommand = new RelayCommand(Cancel, () => IsScanning);
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);
        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        ToggleAboutCommand = new RelayCommand(() => IsAboutOpen = !IsAboutOpen);
        SelectDriveCommand = new RelayCommand(SelectDrive);
        ToggleExternalFilterCommand = new RelayCommand(() => ShowExternalOnly = !ShowExternalOnly);
        GetSmartDataCommand = new RelayCommand(async () => await SafeAsync(GetSmartData, "SmartQuery"),
            () => HasSelectedDrive && !IsLoadingSmartData && !SmartDataQueried);
        ActivateLicenseCommand = new RelayCommand(async () => await SafeAsync(ActivateLicense, "LicenseActivation"), () => CanActivate);
        RevalidateLicenseCommand = new RelayCommand(async () => await SafeAsync(RevalidateLicense, "LicenseRevalidation"), () => !IsActivatingLicense);
        ToggleLicenseSettingsCommand = new RelayCommand(() => IsLicenseSettingsOpen = !IsLicenseSettingsOpen);
        DumpDriveDataCommand = new RelayCommand(async () => await SafeAsync(DumpDriveData, "DumpDriveData"), () => HasSelectedDrive);
        ZoomInCommand = new RelayCommand(() => DetailZoom += 0.1);
        ZoomOutCommand = new RelayCommand(() => DetailZoom -= 0.1);
        ZoomResetCommand = new RelayCommand(() => DetailZoom = 1.1);
        ExportForListingCommand = new RelayCommand(ExportForListing, () => HasSelectedDrive);

        Logger.Info("DriveFlip started.");
        _ = RefreshDrivesAsync();
    }

    // ── License Methods ──

    public void InitializeLicense(LicenseStatusEnum status, LicensePayload? payload)
    {
        LicenseStatusValue = status;
        LicensePayloadValue = payload;

        var settings = _license.LoadSettings();
        LicenseKeyInput = settings.LicenseKey;
    }

    public async Task RevalidateLicenseInBackgroundAsync()
    {
        try
        {
            var (status, payload) = await _license.RevalidateAsync();
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
            LicenseErrorMessage = Loc.Format("LicenseTooManyAttempts", remaining.Minutes, remaining.Seconds);
            return;
        }

        var key = LicenseKeyInput.Trim();

        // Track distinct keys attempted
        _attemptedKeys.Add(key);
        if (_attemptedKeys.Count > MaxActivationAttempts)
        {
            _lockoutUntil = DateTime.UtcNow.Add(ActivationLockoutDuration);
            var remaining = ActivationLockoutDuration;
            LicenseErrorMessage = Loc.Format("LicenseTooManyAttemptsMinutes", remaining.Minutes);
            Logger.Warning($"License activation locked out until {_lockoutUntil.Value:u} after {_attemptedKeys.Count} distinct keys.");
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        var settings = _license.LoadSettings();
        var endpointUrl = settings.EndpointUrl;
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            LicenseErrorMessage = Loc.Get("LicenseNoEndpoint");
            Logger.Warning("License activation attempted without endpoint URL configured.");
            return;
        }

        IsActivatingLicense = true;

        try
        {
            var (status, payload) = await _license.ActivateLicenseAsync(key, endpointUrl);

            LicenseStatusValue = status;
            LicensePayloadValue = payload;

            if (IsLicensed)
            {
                LicenseErrorMessage = "";
                GlobalStatus = Loc.Get("LicenseActivated");
                IsLicenseSettingsOpen = false;
                // Reset rate limiter on success
                _attemptedKeys.Clear();
                _lockoutUntil = null;
            }
            else
            {
                LicenseErrorMessage = status switch
                {
                    LicenseStatusEnum.Expired => Loc.Get("LicenseKeyExpired"),
                    _ => Loc.Get("LicenseKeyInvalid")
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Error("License activation error", ex);
            LicenseErrorMessage = Loc.Format("LicenseActivationFailed", ex.Message);
        }
        finally
        {
            IsActivatingLicense = false;
        }
    }

    private async Task RevalidateLicense()
    {
        IsActivatingLicense = true;
        GlobalStatus = Loc.Get("LicenseRevalidating");

        try
        {
            var (status, payload) = await _license.RevalidateAsync();
            LicenseStatusValue = status;
            LicensePayloadValue = payload;
            GlobalStatus = IsLicensed ? Loc.Get("LicenseRevalidated") : Loc.Get("LicenseRevalidationFailed");
        }
        catch (Exception ex)
        {
            Logger.Error("License revalidation error", ex);
            GlobalStatus = Loc.Format("LicenseRevalidationError", ex.Message);
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

        // Time remaining = max across running drives (parallel ops finish when slowest finishes)
        var remaining = running.Count > 0
            ? running.Max(d => d.TimeRemaining)
            : TimeSpan.Zero;
        if (remaining.TotalSeconds < 1)
            AggregateTimeRemaining = "00:00";
        else if (remaining.TotalHours >= 1)
            AggregateTimeRemaining = $"{(int)remaining.TotalHours}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        else
            AggregateTimeRemaining = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";

        OperationSummary = Loc.Format("DashboardDrivesComplete", completed.Count, total);
    }

    private static TimeSpan EstimateTimeRemaining(OperationProgress p)
    {
        // Use engine-reported estimate when available
        if (p.EstimatedRemaining > TimeSpan.Zero)
            return p.EstimatedRemaining;

        // Fallback: extrapolate from elapsed time and progress
        if (p.PercentComplete > 0.5 && p.Elapsed.TotalSeconds > 1)
        {
            var totalEstimate = p.Elapsed.TotalSeconds / p.PercentComplete * 100;
            var remaining = totalEstimate - p.Elapsed.TotalSeconds;
            return remaining > 0 ? TimeSpan.FromSeconds(remaining) : TimeSpan.Zero;
        }

        return TimeSpan.Zero;
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
        GlobalStatus = Loc.Get("StatusDetectingDrives");

        try
        {
            var detected = await Task.Run(() => _detection.DetectDrives());

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

            GlobalStatus = Loc.Format("StatusFoundDrivesQuerying", detected.Count);

            // Auto-query SMART/health for each drive sequentially with fault isolation
            foreach (var driveVm in Drives.ToList())
            {
                driveVm.IsQueryingHealth = true;
                try
                {
                    var devNum = driveVm.Drive.DeviceNumber;
                    var health = await Task.Run(() => _detection.QueryHealthInfo(devNum));
                    driveVm.Drive.Health = health;

                    // Copy ManufactureDate from health to drive model for Info tab binding
                    if (!string.IsNullOrEmpty(health.ManufactureDate))
                        driveVm.Drive.ManufactureDate = health.ManufactureDate;

                    // Query detailed SMART data for risk assessment
                    var busType = Enum.TryParse<StorageBusType>(health.BusType, true, out var bt)
                        ? bt : StorageBusType.Unknown;
                    await Task.Run(() =>
                        _detection.QueryDetailedSmartData(devNum, health, busType));

                    driveVm.RiskLevel = health.RiskLevel;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Health query failed for disk {driveVm.Drive.DeviceNumber}: {ex.Message}");
                    driveVm.Drive.Health = new DriveHealthInfo { HealthStatus = Loc.Get("StatusQueryFailed") };
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

            GlobalStatus = Loc.Format("StatusFoundDrivesReady", detected.Count);

            // Capture drive snapshot for crash reports
            CrashReportService.SetDriveSnapshot(Drives.Select(d => d.Drive));
        }
        catch (Exception ex)
        {
            GlobalStatus = Loc.Format("StatusErrorDetecting", ex.Message);
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
            var driveList = string.Join("\n", systemDrives.Select(d => $"  - {d.Drive.DisplayName}"));
            Views.StyledDialog.ShowInfo(Loc.Get("DialogSystemDriveProtected"), Loc.Format("DialogSystemDriveExcluded", driveList));
            foreach (var s in systemDrives) s.IsSelected = false;
        }

        return selected;
    }

    private async Task RunSurfaceCheck()
    {
        var selected = GetSelectedNonSystem(out _);
        if (!selected.Any())
        {
            GlobalStatus = Loc.Get("StatusNoSelection");
            return;
        }

        var driveList = string.Join("\n", selected.Select(d => $"  - {d.Drive.DisplayName}"));
        var confirmed = Views.StyledDialog.ShowQuestion(
            Loc.Get("DialogConfirmSurfaceCheck"),
            Loc.Format("DialogSurfaceCheckConfirm", SurfaceCheckDurationMinutes, selected.Count, driveList));

        if (!confirmed) return;

        Logger.Info($"Surface check requested on {selected.Count} drive(s), duration={SurfaceCheckDurationMinutes}min");
        IsScanning = true;
        _operationStartedAt = DateTime.UtcNow;
        _cts = new CancellationTokenSource();
        GlobalStatus = Loc.Get("StatusRunningSurfaceCheck");

        foreach (var d in selected) { d.Reset(); d.IsRunning = true; }
        SelectedDriveItem = selected[0];


        await RunSurfaceCheckOnDrives(selected);

        IsScanning = false;
        if (selected.Any(d => d.IsComplete && !d.Passed))
            SoundService.PlayError();
        else if (!_cts!.IsCancellationRequested)
            SoundService.PlaySuccess();
        GlobalStatus = Loc.Get("StatusSurfaceCheckComplete");
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
                    driveVm.TimeRemaining = EstimateTimeRemaining(p);
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
                    driveVm.StatusText = report.Passed ? Loc.Get("StatusHealthy") : Loc.Get("StatusErrorsFound");
                    driveVm.MarkAllPhasesDone();
                });
            }
            catch (OperationCanceledException)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.IsRunning = false;
                    driveVm.StatusText = Loc.Get("StatusCancelled");
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
                        ? Loc.Get("StatusAccessDenied")
                        : Loc.Format("StatusError", ex.Message);
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
            GlobalStatus = Loc.Get("StatusNoSelection");
            return;
        }

        var modeName = SelectedWipeMode == WipeMode.SmartWipe ? Loc.Get("WipeModeSmartWipe") : Loc.Get("WipeModeFullWipe");
        var methodName = SelectedWipeMethod switch
        {
            WipeMethod.ZeroFill => Loc.Get("WipeMethodZeroFill"),
            WipeMethod.RandomFill => Loc.Get("WipeMethodRandomFill"),
            WipeMethod.RandomThenZero => Loc.Get("WipeMethodRandomZero"),
            _ => Loc.Get("RiskUnknown")
        };

        var verifyText = VerifyAfterWipe ? Loc.Get("DialogVerifyWillRun") : "";

        var driveList = string.Join("\n", selected.Select(d =>
            $"  - {d.Drive.DisplayName} [{d.Drive.DriveLettersSummary}]"));
        var firstConfirmed = Views.StyledDialog.ShowWarning(
            Loc.Get("DialogConfirmWipe"),
            Loc.Format("DialogWipeConfirm", modeName, methodName, NumberOfPasses, verifyText, driveList));

        if (!firstConfirmed) return;

        var finalConfirmed = Views.StyledDialog.ShowDanger(
            Loc.Get("DialogFinalConfirmation"),
            Loc.Format("DialogWipeFinalConfirm", selected.Count));

        if (!finalConfirmed) return;

        Logger.Info($"Wipe requested: mode={modeName}, fill={methodName}, passes={NumberOfPasses}, drives={selected.Count}");
        IsScanning = true;
        _operationStartedAt = DateTime.UtcNow;
        _cts = new CancellationTokenSource();
        GlobalStatus = Loc.Get("StatusWipingDrives");

        foreach (var d in selected) { d.Reset(); d.IsRunning = true; }
        SelectedDriveItem = selected[0];


        await RunWipeOnDrives(selected);

        IsScanning = false;
        if (selected.Any(d => d.IsComplete && !d.Passed))
            SoundService.PlayError();
        else if (!_cts!.IsCancellationRequested)
            SoundService.PlaySuccess();
        GlobalStatus = Loc.Get("StatusWipeComplete");
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RunWipeOnDrives(System.Collections.Generic.List<DriveItemViewModel> selected)
    {
        var settings = BuildSettings();
        bool isSmartWipe = SelectedWipeMode == WipeMode.SmartWipe;
        bool formatAfterWipe = FormatAfterWipe;
        bool assignLetterAfterWipe = AssignLetterAfterWipe;

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
                    driveVm.InitPhases(isSmartWipe, false, VerifyAfterWipe && !isSmartWipe);
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
                    driveVm.TimeRemaining = EstimateTimeRemaining(p);
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

                bool wipeSucceeded = report.Completed && report.WriteErrors == 0;

                // Post-wipe: quick format & assign letter
                if (wipeSucceeded && formatAfterWipe)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        driveVm.StatusText = Loc.Get("StatusFormatting");
                        driveVm.SpeedText = "";
                    });

                    Logger.Info($"Disk {driveVm.Drive.DeviceNumber}: Starting post-wipe format (assignLetter={assignLetterAfterWipe})...");
                    var (fmtOk, letter, fmtError) = await Task.Run(() =>
                        DriveDetectionService.InitializeFormatAndAssignLetter(driveVm.Drive.DeviceNumber, assignLetterAfterWipe));

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        driveVm.IsRunning = false;
                        driveVm.IsComplete = true;
                        driveVm.Passed = fmtOk;
                        driveVm.ReportText = ReportService.GenerateWipeReport(report);
                        driveVm.StatusText = fmtOk
                            ? Loc.Format("StatusWipeFormatted", letter)
                            : Loc.Format("StatusWipeFormatFailed", fmtError);
                        driveVm.MarkAllPhasesDone();

                        if (!fmtOk)
                        {
                            Views.StyledDialog.ShowWarning(
                                Loc.Format("DialogFormatFailedTitle", driveVm.Drive.DeviceNumber),
                                Loc.Format("DialogFormatFailedMessage", driveVm.Drive.DisplayName, fmtError));
                        }
                    });
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        driveVm.IsRunning = false;
                        driveVm.IsComplete = true;
                        driveVm.Passed = wipeSucceeded;
                        driveVm.ReportText = ReportService.GenerateWipeReport(report);
                        driveVm.StatusText = report.Completed ? Loc.Get("StatusWipeCompleteShort") : Loc.Get("StatusWipeIncomplete");
                        driveVm.MarkAllPhasesDone();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    driveVm.IsRunning = false;
                    driveVm.StatusText = Loc.Get("StatusCancelled");
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
                        ? Loc.Get("StatusAccessDenied")
                        : Loc.Format("StatusError", ex.Message);
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
            GlobalStatus = Loc.Get("StatusNoSelection");
            return;
        }

        var modeName = SelectedWipeMode == WipeMode.SmartWipe ? Loc.Get("WipeModeSmartWipe") : Loc.Get("WipeModeFullWipe");

        var driveList = string.Join("\n", selected.Select(d => $"  - {d.Drive.DisplayName}"));
        var confirmed = Views.StyledDialog.ShowQuestion(
            Loc.Get("DialogConfirmCheckWipe"),
            Loc.Format("DialogCheckWipeConfirm", SurfaceCheckDurationMinutes, modeName, driveList));

        if (!confirmed) return;

        Logger.Info($"Check & Wipe requested on {selected.Count} drive(s)");
        IsScanning = true;
        _operationStartedAt = DateTime.UtcNow;
        _cts = new CancellationTokenSource();

        // ── Phase 1: Surface Check ──
        GlobalStatus = Loc.Get("StatusPhase1");
        bool isSmartWipe = SelectedWipeMode == WipeMode.SmartWipe;
        foreach (var d in selected) { d.Reset(); d.IsRunning = true; }
        foreach (var d in selected)
            d.InitPhases(isSmartWipe, false, VerifyAfterWipe && !isSmartWipe, isCheckAndWipe: true);
        SelectedDriveItem = selected[0];


        await RunSurfaceCheckOnDrives(selected);

        if (_cts.IsCancellationRequested)
        {
            IsScanning = false;
            GlobalStatus = Loc.Get("StatusCheckWipeCancelled");
            return;
        }

        // ── Evaluate results ──
        var failed = selected.Where(d => !d.Passed).ToList();
        if (failed.Any())
        {
            var failedList = string.Join("\n", failed.Select(d => $"  - {d.Drive.DisplayName}: {d.StatusText}"));
            var continueWipe = Views.StyledDialog.ShowWarning(
                Loc.Get("DialogIssuesFoundContinue"),
                Loc.Format("DialogIssuesFoundMessage", failed.Count, failedList));

            if (!continueWipe)
            {
                IsScanning = false;
                GlobalStatus = Loc.Get("StatusCheckWipeStopped");
                CommandManager.InvalidateRequerySuggested();
                return;
            }
        }

        // ── Phase 2: Wipe ──
        GlobalStatus = Loc.Get("StatusPhase2");
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


        await RunWipeOnDrives(selected);

        IsScanning = false;
        if (selected.Any(d => d.IsComplete && !d.Passed))
            SoundService.PlayError();
        else if (!_cts!.IsCancellationRequested)
            SoundService.PlaySuccess();
        GlobalStatus = Loc.Get("StatusCheckWipeComplete");
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task GetSmartData()
    {
        var drive = SelectedDriveItem?.Drive;
        if (drive?.Health == null) return;

        IsLoadingSmartData = true;
        try
        {
            var busType = Enum.TryParse<StorageBusType>(drive.Health.BusType, true, out var bt)
                ? bt : StorageBusType.Unknown;
            await Task.Run(() =>
                _detection.QueryDetailedSmartData(drive.DeviceNumber, drive.Health, busType));

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
            GlobalStatus = Loc.Format("StatusSmartQueryFailed", drive.DeviceNumber, ex.Message);
        }
        finally
        {
            IsLoadingSmartData = false;
        }
    }

    private async Task DumpDriveData()
    {
        var drive = SelectedDriveItem?.Drive;
        if (drive == null) return;

        GlobalStatus = Loc.Format("StatusDumpingData", drive.DeviceNumber);
        var json = await Task.Run(() => _detection.DumpDriveData(drive.DeviceNumber));

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"DriveFlip_Disk{drive.DeviceNumber}_Dump.json",
            DefaultExt = ".json",
            Filter = Loc.Get("JsonFilter")
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, json);
            GlobalStatus = Loc.Format("StatusDataDumped", dialog.FileName);

            // Open the file
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
            catch { }
        }
        else
        {
            GlobalStatus = Loc.Get("StatusDataDumpCancelled");
        }
    }

    private void ExportForListing()
    {
        var drive = SelectedDrive;
        if (drive == null) return;

        if (Views.ListingExportDialog.ShowExport(drive))
            GlobalStatus = Loc.Get("StatusListingCopied");
    }

    private void Cancel()
    {
        _cts?.Cancel();
        GlobalStatus = Loc.Get("StatusCancelling");
        Logger.Info("Operation cancelled by user.");
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
                    var health = _detection.QueryHealthInfo(driveVm.Drive.DeviceNumber);
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
                        driveVm.Drive.Health = new DriveHealthInfo { HealthStatus = Loc.Get("StatusQueryFailed") };
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
    private async Task SafeAsync(Func<Task> action, string operation = "")
    {
        CrashReportService.ActiveOperation = operation;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logger.Error("Unhandled error in async command", ex);
            CrashReportService.WriteCrashFile(ex, "SafeAsync");
            SoundService.PlayError();
            GlobalStatus = Loc.Format("StatusError", ex.Message);
            IsScanning = false;
        }
        finally
        {
            CrashReportService.ActiveOperation = "";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
