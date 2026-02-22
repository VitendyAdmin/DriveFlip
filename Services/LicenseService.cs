using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DriveFlip.Models;

namespace DriveFlip.Services;

public static class LicenseService
{
    private const string EmbeddedPublicKeyBase64 = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAn8rtg2tce60D4mNWpP2AyUb/JLa+tBasdFIY4UM9D9VCHIX/DE0Oo9IJuCPjeEtZLpm/aofHOrrmVei2fV9UVgEPi4ZVeSJ0DHXuXsJRvZJaoc7DTT6zOkGayg0Lz0cfLnrbnzAkFoxrM/k41ka7QUmNtzxpeVyq1Y1WWRowmIvUcQ5LCF8imOdjAt4d3RNEj7qsgKgFOnBMtM44rhEJLvgkTvbegSGUTJZpT6bKqNKNnWyNpM4/tBcw/Ig4bG5o6BwPkplSJ4DKyhnKT6+ApQ+xywhg8X5RI8aApdrNUMnD3Hx21qIWW2At/i2J3XxcSZCTHCfBsodnOqZPfVDSoQIDAQAB";

    private const byte ChecksumSalt = 0xA7;

    /// <summary>
    /// Pre-flight check: the last byte of the GUID must be a checksum of the first 15 bytes
    /// XOR'd with a salt. Filters out ~255/256 of random/made-up GUIDs without a server call.
    /// </summary>
    public static bool IsWellFormedLicenseKey(string key)
    {
#if DEBUG
        return Guid.TryParse(key, out _);
#else
        if (!Guid.TryParse(key, out var guid))
            return false;

        var bytes = guid.ToByteArray();
        byte checksum = ChecksumSalt;
        for (int i = 0; i < 15; i++)
            checksum ^= bytes[i];

        return bytes[15] == checksum;
#endif
    }

    /// <summary>
    /// Generates a GUID with an embedded checksum in the last byte.
    /// Used by the key generator — not called at runtime.
    /// </summary>
    public static Guid GenerateLicenseGuid()
    {
        var bytes = Guid.NewGuid().ToByteArray();

        byte checksum = ChecksumSalt;
        for (int i = 0; i < 15; i++)
            checksum ^= bytes[i];

        bytes[15] = checksum;
        return new Guid(bytes);
    }

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriveFlip");

    private static readonly string CachePath = Path.Combine(AppDataDir, "license.json");
    private static readonly string SettingsPath = Path.Combine(AppDataDir, "license-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Public API ──

    public static (LicenseStatus Status, LicensePayload? Payload) LoadCachedLicense()
    {
        try
        {
            if (!File.Exists(CachePath))
                return (LicenseStatus.Unknown, null);

            var json = File.ReadAllText(CachePath);
            var response = JsonSerializer.Deserialize<SignedLicenseResponse>(json, JsonReadOptions);
            if (response?.Payload == null)
                return (LicenseStatus.Invalid, null);

#if !DEBUG
            if (!VerifySignature(response.Payload, response.Signature))
            {
                Logger.Warning("Cached license signature verification failed.");
                return (LicenseStatus.Invalid, null);
            }
#endif

            if (response.Payload.ExpiresUtc < DateTime.UtcNow)
            {
                Logger.Info("Cached license is expired.");
                return (LicenseStatus.Expired, response.Payload);
            }

            Logger.Info($"Cached license loaded: {response.Payload.Licensee}, edition={response.Payload.Edition}, expires={response.Payload.ExpiresUtc:u}");
            return (LicenseStatus.CachedOffline, response.Payload);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load cached license", ex);
            return (LicenseStatus.Unknown, null);
        }
    }

    public static async Task<(LicenseStatus Status, LicensePayload? Payload)> ActivateLicenseAsync(
        string key, string endpointUrl)
    {
        if (!Guid.TryParse(key, out _))
        {
            Logger.Warning($"Invalid license key format: {key}");
            return (LicenseStatus.Invalid, null);
        }

#if DEBUG
        await Task.Delay(800); // simulate network latency
        var mockPayload = new LicensePayload
        {
            LicenseKey = key,
            Licensee = "Developer (Debug)",
            Email = "dev@localhost",
            Product = "DriveFlip",
            Edition = "Professional",
            MaxMachines = 99,
            ExpiresUtc = DateTime.UtcNow.AddYears(1),
            IssuedUtc = DateTime.UtcNow
        };
        SaveCache(new SignedLicenseResponse { Payload = mockPayload, Signature = "DEBUG" });
        SaveSettings(new LicenseSettings { LicenseKey = key, EndpointUrl = endpointUrl, LastOnlineCheckUtc = DateTime.UtcNow });
        Logger.Info("DEBUG: Mock license activated and cached.");
        return (LicenseStatus.Valid, mockPayload);
#else
        try
        {
            var response = await FetchFromEndpointAsync(endpointUrl, key);
            if (response?.Payload == null)
                return (LicenseStatus.Invalid, null);

            if (!VerifySignature(response.Payload, response.Signature))
            {
                Logger.Warning("License signature verification failed during activation.");
                return (LicenseStatus.Invalid, null);
            }

            if (response.Payload.ExpiresUtc < DateTime.UtcNow)
            {
                Logger.Info("Activated license is expired.");
                return (LicenseStatus.Expired, response.Payload);
            }

            // Cache the signed response
            SaveCache(response);

            // Save settings
            SaveSettings(new LicenseSettings
            {
                LicenseKey = key,
                EndpointUrl = endpointUrl,
                LastOnlineCheckUtc = DateTime.UtcNow
            });

            Logger.Info($"License activated: {response.Payload.Licensee}, edition={response.Payload.Edition}");
            return (LicenseStatus.Valid, response.Payload);
        }
        catch (Exception ex)
        {
            Logger.Error("License activation failed", ex);
            return (LicenseStatus.Invalid, null);
        }
#endif
    }

    public static async Task<(LicenseStatus Status, LicensePayload? Payload)> RevalidateAsync()
    {
        var settings = LoadSettings();
        if (string.IsNullOrEmpty(settings.LicenseKey) || string.IsNullOrEmpty(settings.EndpointUrl))
            return (LicenseStatus.Unknown, null);

        try
        {
            var response = await FetchFromEndpointAsync(settings.EndpointUrl, settings.LicenseKey);
            if (response?.Payload == null)
                return (LicenseStatus.Invalid, null);

            if (!VerifySignature(response.Payload, response.Signature))
            {
                Logger.Warning("License signature verification failed during revalidation.");
                return (LicenseStatus.Invalid, null);
            }

            if (response.Payload.ExpiresUtc < DateTime.UtcNow)
                return (LicenseStatus.Expired, response.Payload);

            SaveCache(response);
            settings.LastOnlineCheckUtc = DateTime.UtcNow;
            SaveSettings(settings);

            Logger.Info("License revalidated successfully.");
            return (LicenseStatus.Valid, response.Payload);
        }
        catch (Exception ex)
        {
            Logger.Warning($"License revalidation failed (offline?): {ex.Message}");
            // Fall back to cached license
            return LoadCachedLicense();
        }
    }

    public static bool IsOnlineRecheckDue()
    {
        var settings = LoadSettings();
        return (DateTime.UtcNow - settings.LastOnlineCheckUtc).TotalHours >= 24;
    }

    // ── Settings ──

    public static LicenseSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new LicenseSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<LicenseSettings>(json, JsonReadOptions) ?? new LicenseSettings();
        }
        catch
        {
            return new LicenseSettings();
        }
    }

    public static void SaveSettings(LicenseSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save license settings", ex);
        }
    }

    // ── Internals ──

    private static bool VerifySignature(LicensePayload payload, string signatureBase64)
    {
        try
        {
            if (string.IsNullOrEmpty(signatureBase64))
                return false;

            var canonicalJson = JsonSerializer.Serialize(payload, JsonOptions);
            var data = Encoding.UTF8.GetBytes(canonicalJson);
            var signature = Convert.FromBase64String(signatureBase64);

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(EmbeddedPublicKeyBase64), out _);

            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            Logger.Error("RSA signature verification error", ex);
            return false;
        }
    }

    private static async Task<SignedLicenseResponse?> FetchFromEndpointAsync(string endpointUrl, string key)
    {
        string json;

        if (endpointUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            endpointUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var requestUrl = endpointUrl.TrimEnd('/') + "?key=" + Uri.EscapeDataString(key);
            json = await client.GetStringAsync(requestUrl);
        }
        else
        {
            // Local file path — for dev/mock
            if (!File.Exists(endpointUrl))
            {
                Logger.Warning($"License endpoint file not found: {endpointUrl}");
                return null;
            }
            json = await File.ReadAllTextAsync(endpointUrl);
        }

        return JsonSerializer.Deserialize<SignedLicenseResponse>(json, JsonReadOptions);
    }

    private static void SaveCache(SignedLicenseResponse response)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(response, JsonOptions);
            File.WriteAllText(CachePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save license cache", ex);
        }
    }
}
