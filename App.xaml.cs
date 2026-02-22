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

        // Load cached license (sync — just file read + RSA verify)
        var (status, payload) = LicenseService.LoadCachedLicense();

        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        vm.InitializeLicense(status, payload);

        MainWindow = window;
        window.Show();

        // If licensed and online re-check is due, fire background revalidation
        if (vm.IsLicensed && LicenseService.IsOnlineRecheckDue())
        {
            _ = vm.RevalidateLicenseInBackgroundAsync();
        }
    }
}
