using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DriveFlip.Services;

/// <summary>
/// Replays drive data from a JSON dump file.
/// Enables reproducing bugs without physical hardware:
///   customer exports dump → developer loads dump → sees exactly what the app shows.
/// Backward-compatible with old dumps — missing keys return null/empty.
/// </summary>
public class JsonDriveDataProvider : IDriveDataProvider
{
    private readonly Dictionary<string, JsonElement> _dump;

    public JsonDriveDataProvider(string json)
    {
        _dump = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
            ?? throw new ArgumentException("Invalid JSON dump: root must be an object.");
    }

    // ── Helpers ──

    private static object? NormalizeJsonElement(JsonElement elem)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => elem.TryGetInt64(out var l) ? l : (object)elem.GetDouble(),
            JsonValueKind.String => elem.GetString(),
            JsonValueKind.Array => elem.EnumerateArray().Select(NormalizeJsonElement).ToList(),
            JsonValueKind.Object => ToPropertyBag(elem),
            _ => elem.ToString()
        };
    }

    private static Dictionary<string, object?> ToPropertyBag(JsonElement obj)
    {
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.EnumerateObject())
            bag[prop.Name] = NormalizeJsonElement(prop.Value);
        return bag;
    }

    private bool TryGetElement(string key, out JsonElement elem)
    {
        return _dump.TryGetValue(key, out elem) && elem.ValueKind != JsonValueKind.Null;
    }

    private byte[]? GetBase64(string key)
    {
        if (TryGetElement(key, out var elem) && elem.ValueKind == JsonValueKind.String)
        {
            var str = elem.GetString();
            if (!string.IsNullOrEmpty(str))
            {
                try { return Convert.FromBase64String(str); }
                catch { }
            }
        }
        return null;
    }

    // ── WMI Property Bags ──

    public List<Dictionary<string, object?>> GetWin32DiskDrives()
    {
        if (TryGetElement("Win32_DiskDrive", out var elem))
        {
            if (elem.ValueKind == JsonValueKind.Object)
                return [ToPropertyBag(elem)];
            if (elem.ValueKind == JsonValueKind.Array)
                return elem.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(ToPropertyBag).ToList();
        }
        return [];
    }

    public Dictionary<string, object?>? GetPhysicalDisk(int deviceNumber)
    {
        if (TryGetElement("MSFT_PhysicalDisk", out var elem) && elem.ValueKind == JsonValueKind.Object)
            return ToPropertyBag(elem);
        return null;
    }

    public Dictionary<string, object?>? GetReliabilityCounters(int deviceNumber)
    {
        if (TryGetElement("MSFT_StorageReliabilityCounter", out var elem) && elem.ValueKind == JsonValueKind.Object)
            return ToPropertyBag(elem);
        return null;
    }

    // ── Drive Letters ──

    public (List<string> Partitions, List<string> DriveLetters) GetDriveLettersWin32(string devicePath)
    {
        // Win32 chain data isn't in the dump — return empty so MSFT fallback is used
        return (new List<string>(), new List<string>());
    }

    public (List<string> Partitions, List<string> DriveLetters) GetDriveLettersMsft(int deviceNumber)
    {
        var partitions = new List<string>();
        var letters = new List<string>();

        // Try structured DriveLetters key (v2 dump format)
        if (TryGetElement("DriveLetters", out var dlElem) && dlElem.ValueKind == JsonValueKind.Object)
        {
            if (dlElem.TryGetProperty("Partitions", out var partsElem) && partsElem.ValueKind == JsonValueKind.Array)
                partitions.AddRange(partsElem.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!));

            if (dlElem.TryGetProperty("Letters", out var lettersElem) && lettersElem.ValueKind == JsonValueKind.Array)
                letters.AddRange(lettersElem.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!));

            return (partitions, letters);
        }

        // Fallback: extract from MSFT_Partitions array (v1 dump format)
        if (TryGetElement("MSFT_Partitions", out var partsArray) && partsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in partsArray.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object) continue;

                if (part.TryGetProperty("PartitionNumber", out var pn))
                    partitions.Add($"Disk #{deviceNumber}, Partition #{pn}");

                if (part.TryGetProperty("DriveLetter", out var dl))
                {
                    var letter = dl.ValueKind == JsonValueKind.String
                        ? dl.GetString()
                        : dl.ValueKind == JsonValueKind.Number && dl.TryGetInt64(out var code) && code > 0
                            ? $"{(char)code}:"
                            : null;
                    if (!string.IsNullOrEmpty(letter) && letter != "\0:" && letter != "\0")
                        letters.Add(letter.Contains(':') ? letter : $"{letter}:");
                }
            }
        }

        return (partitions, letters);
    }

    // ── Partition Style ──

    public int? GetPartitionStyle(int deviceNumber)
    {
        if (TryGetElement("MSFT_Disk", out var elem) && elem.ValueKind == JsonValueKind.Object)
        {
            if (elem.TryGetProperty("PartitionStyle", out var ps) && ps.ValueKind == JsonValueKind.Number)
            {
                if (ps.TryGetInt32(out var style))
                    return style;
            }
        }
        return null;
    }

    // ── ATA IDENTIFY ──

    public byte[]? GetAtaIdentifyViaSat(int deviceNumber)
    {
        return GetBase64("ATA_Identify_Raw");
    }

    public byte[]? GetAtaIdentifyViaSmart(int deviceNumber)
    {
        return GetBase64("ATA_Identify_Raw");
    }

    // ── USB Bridge ──

    public (string Vid, string Pid)? GetUsbBridgeVidPid(string pnpDeviceId)
    {
        if (TryGetElement("USB_Bridge_VID", out var vidElem) && vidElem.ValueKind == JsonValueKind.String
            && TryGetElement("USB_Bridge_PID", out var pidElem) && pidElem.ValueKind == JsonValueKind.String)
        {
            var vid = vidElem.GetString();
            var pid = pidElem.GetString();
            if (!string.IsNullOrEmpty(vid) && !string.IsNullOrEmpty(pid))
                return (vid, pid);
        }
        return null;
    }

    // ── SMART / NVMe ──

    public (byte[]? Attributes, byte[]? Thresholds) GetSmartData(int deviceNumber)
    {
        return (GetBase64("SMART_Attributes_Raw"), GetBase64("SMART_Thresholds_Raw"));
    }

    public byte[]? GetNvmeHealthLog(int deviceNumber)
    {
        return GetBase64("NVMe_HealthLog_Raw");
    }

    // ── Dump ──

    public Dictionary<string, object?> GetFullDump(int deviceNumber)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in _dump)
            result[key] = NormalizeJsonElement(value);
        return result;
    }
}
