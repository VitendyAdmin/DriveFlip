using System;
using System.Collections.Generic;

namespace DriveFlip.Models;

public enum LicenseStatus
{
    Unknown,
    Valid,
    Expired,
    Invalid,
    CachedOffline
}

public class LicensePayload
{
    public string LicenseKey { get; set; } = "";
    public string Licensee { get; set; } = "";
    public string Email { get; set; } = "";
    public string Product { get; set; } = "";
    public string Edition { get; set; } = "";
    public int MaxMachines { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime IssuedUtc { get; set; }
    public List<string> Features { get; set; } = new();
}

public class SignedLicenseResponse
{
    public LicensePayload? Payload { get; set; }
    public string Signature { get; set; } = "";
}

public class LicenseSettings
{
    public string LicenseKey { get; set; } = "";
    /// <summary>
    /// License server URL (HTTP/HTTPS) or local file path for dev/mock testing.
    /// Configured in %LOCALAPPDATA%\DriveFlip\license-settings.json — not exposed in the GUI.
    /// </summary>
    public string EndpointUrl { get; set; } = "";
    public DateTime LastOnlineCheckUtc { get; set; }
}
