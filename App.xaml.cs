using System.Runtime.Versioning;
using System.Windows;
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

        // If licensed and online re-check is due, fire background revalidation
        if (vm.IsLicensed && license.IsOnlineRecheckDue())
        {
            _ = vm.RevalidateLicenseInBackgroundAsync();
        }
    }
}
