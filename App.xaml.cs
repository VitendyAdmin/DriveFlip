using System.Runtime.Versioning;
using System.Windows;
using DriveFlip.Localization;
using DriveFlip.Services;
using DriveFlip.ViewModels;
using DriveFlip.Views;

namespace DriveFlip;

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // Load cached license (sync — just file read + RSA verify)
        var (status, payload) = license.LoadCachedLicense();

        var vm = new MainViewModel(detection, engine, license);
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
}
