using System.Threading.Tasks;
using DriveFlip.Models;

namespace DriveFlip.Services;

public interface ILicenseService
{
    bool IsWellFormedLicenseKey(string key);
    (LicenseStatus Status, LicensePayload? Payload) LoadCachedLicense();
    Task<(LicenseStatus Status, LicensePayload? Payload)> ActivateLicenseAsync(string key, string endpointUrl);
    Task<(LicenseStatus Status, LicensePayload? Payload)> RevalidateAsync();
    bool IsOnlineRecheckDue();
    LicenseSettings LoadSettings();
    void SaveSettings(LicenseSettings settings);
}
