using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using DriveFlip.Localization;
using DriveFlip.Models;
using DriveFlip.Views;

namespace DriveFlip.Services;

[SupportedOSPlatform("windows")]
public static class CrashReportService
{
    private static readonly string CrashReportsDir;
    private static readonly string SessionId = Guid.NewGuid().ToString("N")[..12];
    private static readonly DateTime StartedAtUtc = DateTime.UtcNow;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Regex UserPathRegex = new(
        @"(?:C:|\\\\)[\\/]Users[\\/][^\\/\s""]+[\\/]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LicenseKeyRegex = new(
        @"\b(license|key)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int SchemaVersion = 1;
    private const int MaxCrashFiles = 20;
    private const int MaxAgeDays = 30;
    private const int LogTailCount = 200;

    private static List<object>? _driveSnapshot;
    private static bool _crashFileWritten;

    public static string ActiveOperation { get; set; } = "";

    static CrashReportService()
    {
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DriveFlip");
        CrashReportsDir = Path.Combine(appDir, "CrashReports");
    }

    // ── Drive Snapshot ──

    public static void SetDriveSnapshot(IEnumerable<PhysicalDrive> drives)
    {
        _driveSnapshot = drives.Select(d => (object)new
        {
            disk = d.DeviceNumber,
            model = d.Model,
            manufacturer = d.Manufacturer,
            size = d.DisplaySize,
            busType = d.Health?.BusType ?? d.InterfaceType,
            mediaType = d.Health?.MediaType ?? d.MediaType,
            isSystem = d.IsSystemDrive,
            isRemovable = d.IsRemovable,
            driveLetters = d.DriveLetters,
            healthStatus = d.Health?.HealthStatus ?? "N/A",
            riskLevel = d.Health?.RiskLevel.ToString() ?? "Unknown",
            temperature = d.Health?.Temperature,
            powerOnHours = d.Health?.PowerOnHours
        }).ToList();
    }

    // ── Crash-Time: Write Local File ──

    public static void WriteCrashFile(Exception ex, string source)
    {
        try
        {
            // One file per session — prevent crash-loop flooding
            if (_crashFileWritten) return;
            _crashFileWritten = true;

            Directory.CreateDirectory(CrashReportsDir);

            // Enforce max file cap (delete oldest first)
            EnforceFileCap();

            var appVersion = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "2.0.0";

            var crashData = new JsonObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["sessionId"] = SessionId,
                ["appVersion"] = appVersion,
                ["osVersion"] = RuntimeInformation.OSDescription,
                ["dotnetVersion"] = RuntimeInformation.FrameworkDescription,
                ["startedAt"] = StartedAtUtc.ToString("o"),
                ["crashedAt"] = DateTime.UtcNow.ToString("o"),
                ["source"] = source,
                ["activeOperation"] = ActiveOperation,
                ["exception"] = new JsonObject
                {
                    ["type"] = ex.GetType().FullName,
                    ["message"] = RedactPii(ex.Message),
                    ["stackTrace"] = RedactPii(ex.StackTrace ?? "")
                }
            };

            // Connected drives snapshot
            if (_driveSnapshot != null)
            {
                var drivesJson = JsonSerializer.Serialize(_driveSnapshot);
                crashData["connectedDrives"] = JsonNode.Parse(drivesJson);
            }

            var json = crashData.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var filePath = Path.Combine(CrashReportsDir, $"crash-{SessionId}.json");
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }
        catch
        {
            // Never throw from crash reporter
        }
    }

    // ── Next Startup: Handshake → Consent → Upload → Cleanup ──

    public static async Task SendPendingAsync()
    {
        try
        {
            if (!Directory.Exists(CrashReportsDir)) return;

            // Age-cleanup: delete files older than 30 days
            CleanupOldFiles();

            var crashFiles = Directory.GetFiles(CrashReportsDir, "crash-*.json");
            if (crashFiles.Length == 0) return;

            // Check if crash reporting is enabled
            if (!AppSettings.LoadCrashReportEnabled())
            {
                DeleteAllCrashFiles(crashFiles);
                return;
            }

            var endpointUrl = AppSettings.LoadCrashReportEndpointUrl();
            if (string.IsNullOrWhiteSpace(endpointUrl))
            {
                // No endpoint configured — leave files for age-cleanup only
                return;
            }

            // Handshake
            var (handshakeOk, _) = await HandshakeAsync(endpointUrl);
            if (!handshakeOk)
            {
                // Leave files for next attempt
                return;
            }

            // Show consent dialog
            var send = Application.Current.Dispatcher.Invoke(() =>
                StyledDialog.ShowQuestion(
                    Loc.Get("CrashReportDialogTitle"),
                    Loc.Get("CrashReportDialogMessage")));

            if (!send)
            {
                DeleteAllCrashFiles(crashFiles);
                return;
            }

            // Upload each crash file
            foreach (var filePath in crashFiles)
            {
                try
                {
                    var success = await UploadCrashFileAsync(filePath, endpointUrl);
                    if (success)
                    {
                        File.Delete(filePath);
                    }
                }
                catch
                {
                    // Leave file for next attempt
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"SendPendingAsync failed: {ex.Message}");
        }
    }

    // ── Handshake ──

    private static async Task<(bool Ok, string? Reason)> HandshakeAsync(string endpointUrl)
    {
        try
        {
            var appVersion = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "2.0.0";

            var handshakeUrl = $"{endpointUrl.TrimEnd('/')}/handshake?clientVersion={appVersion}&schemaVersion={SchemaVersion}";
            var response = await Client.GetAsync(handshakeUrl);

            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("status", out var statusProp) || statusProp.GetString() != "ok")
                return (false, "status != ok");

            if (!root.TryGetProperty("schemaVersion", out var schemaProp) || schemaProp.GetInt32() != SchemaVersion)
                return (false, "schema mismatch");

            // Version range check
            if (root.TryGetProperty("minClientVersion", out var minProp) &&
                root.TryGetProperty("maxClientVersion", out var maxProp))
            {
                var min = minProp.GetString();
                var max = maxProp.GetString();
                if (min != null && max != null)
                {
                    if (!IsVersionInRange(appVersion, min, max))
                        return (false, "client version out of range");
                }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ── Upload ──

    private static async Task<bool> UploadCrashFileAsync(string filePath, string endpointUrl)
    {
        var crashJson = File.ReadAllText(filePath);
        var crashNode = JsonNode.Parse(crashJson)?.AsObject();
        if (crashNode == null) return false;

        // Append redacted log tail at send time
        crashNode["logTail"] = new JsonArray(
            GetRedactedLogTail().Select(line => (JsonNode)JsonValue.Create(line)!).ToArray());

        var payload = crashNode.ToJsonString();
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await Client.PostAsync(endpointUrl.TrimEnd('/'), content);
        return response.IsSuccessStatusCode;
    }

    // ── PII Redaction ──

    private static string RedactPii(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return UserPathRegex.Replace(text, m =>
        {
            // Preserve the prefix (C:\ or \\) and Users\ part, replace username
            var match = m.Value;
            var usersIdx = match.IndexOf("Users", StringComparison.OrdinalIgnoreCase);
            if (usersIdx < 0) return match;
            var prefix = match[..(usersIdx + 6)]; // "...Users\" or "...Users/"
            var sep = match.Contains('/') ? "/" : "\\";
            return $"{prefix}***{sep}";
        });
    }

    // ── Log Tail ──

    private static string[] GetRedactedLogTail()
    {
        try
        {
            var lines = Logger.ReadTailLines(LogTailCount);
            return lines
                .Where(line => !LicenseKeyRegex.IsMatch(line))
                .Select(RedactPii)
                .ToArray();
        }
        catch { return []; }
    }

    // ── Cleanup Helpers ──

    private static void EnforceFileCap()
    {
        try
        {
            var files = new DirectoryInfo(CrashReportsDir)
                .GetFiles("crash-*.json")
                .OrderBy(f => f.CreationTimeUtc)
                .ToList();

            // Delete oldest until we're under the cap (leaving room for the new file)
            while (files.Count >= MaxCrashFiles)
            {
                files[0].Delete();
                files.RemoveAt(0);
            }
        }
        catch { }
    }

    private static void CleanupOldFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-MaxAgeDays);
            foreach (var file in new DirectoryInfo(CrashReportsDir).GetFiles("crash-*.json"))
            {
                if (file.CreationTimeUtc < cutoff)
                    file.Delete();
            }
        }
        catch { }
    }

    private static void DeleteAllCrashFiles(string[] files)
    {
        foreach (var f in files)
        {
            try { File.Delete(f); }
            catch { }
        }
    }

    // ── Version Comparison ──

    private static bool IsVersionInRange(string version, string min, string max)
    {
        try
        {
            var v = ParseVersion(version);
            var vMin = ParseVersion(min);
            var vMax = ParseVersion(max);
            return v >= vMin && v <= vMax;
        }
        catch { return false; }
    }

    private static Version ParseVersion(string version)
    {
        // Handle 2/3/4-part versions
        var parts = version.Split('.');
        return parts.Length switch
        {
            1 => new Version(int.Parse(parts[0]), 0, 0),
            2 => new Version(int.Parse(parts[0]), int.Parse(parts[1]), 0),
            _ => new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]))
        };
    }
}
