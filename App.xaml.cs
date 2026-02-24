using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows;
using DriveFlip.Localization;
using DriveFlip.Services;
using DriveFlip.ViewModels;
using DriveFlip.Views;

namespace DriveFlip;

/// <summary>
/// Exit codes for Windows Store installer handling.
/// </summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int UserCancelled = 1602;
    public const int AlreadyRunning = 1618;
}

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance check
        _singleInstanceMutex = new Mutex(true, "Global\\DriveFlip_B7A3F1E0", out bool createdNew);
        if (!createdNew)
        {
            Environment.ExitCode = ExitCodes.AlreadyRunning;
            Shutdown(ExitCodes.AlreadyRunning);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Fatal("Unhandled UI exception: " + args.Exception);
            CrashReportService.WriteCrashFile(args.Exception, "DispatcherUnhandled");
        };
        System.AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is System.Exception ex)
            {
                Logger.Fatal("Unhandled domain exception: " + ex);
                CrashReportService.WriteCrashFile(ex, "AppDomainUnhandled");
            }
        };

        // Apply saved language before any UI is created
        Loc.SetLanguage(AppSettings.LoadLanguage());

        var license = new LicenseService();
        var detection = new DriveDetectionService();
        var engine = new DiskEngine();
        var driveLog = new DriveLogService();
        driveLog.Load();

        // Load cached license (sync — just file read + RSA verify)
        var (status, payload) = license.LoadCachedLicense();

        var vm = new MainViewModel(detection, engine, license, driveLog);
        vm.InitializeLicense(status, payload);

        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();

        // Process any pending crash reports from previous sessions
        _ = CrashReportService.SendPendingAsync();

        // If licensed and online re-check is due, fire background revalidation
        if (vm.IsLicensed && license.IsOnlineRecheckDue())
        {
            _ = vm.RevalidateLicenseInBackgroundAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
