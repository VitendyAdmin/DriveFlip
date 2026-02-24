using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DriveFlip.Services;

public static class AppSettings
{
    private static readonly string SettingsPath;

    static AppSettings()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DriveFlip");
        Directory.CreateDirectory(dir);
        SettingsPath = Path.Combine(dir, "app-settings.json");
    }

    public static string LoadLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return "en";
            var json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("language", out var lang))
                return lang.GetString() ?? "en";
        }
        catch { }
        return "en";
    }

    public static bool LoadCrashReportEnabled()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return true;
            var json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("crashReportEnabled", out var val))
                return val.GetBoolean();
        }
        catch { }
        return true;
    }

    public static string LoadCrashReportEndpointUrl()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return "";
            var json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("crashReportEndpointUrl", out var val))
                return val.GetString() ?? "";
        }
        catch { }
        return "";
    }

    public static void Save(string languageCode)
    {
        SaveProperty("language", languageCode);
    }

    public static void SaveCrashReportEnabled(bool enabled)
    {
        SaveProperty("crashReportEnabled", enabled);
    }

    private static void SaveProperty(string key, JsonNode value)
    {
        try
        {
            var obj = LoadAll();
            obj[key] = value;
            var json = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private static JsonObject LoadAll()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new JsonObject();
            var json = File.ReadAllText(SettingsPath);
            return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        }
        catch { return new JsonObject(); }
    }
}
