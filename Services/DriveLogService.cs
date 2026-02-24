using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DriveFlip.Models;

namespace DriveFlip.Services;

public class DriveLogService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriveFlip");
    private static readonly string JsonPath = Path.Combine(DataDir, "drive-log.json");
    private static readonly string PhotosRoot = Path.Combine(DataDir, "photos");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _lock = new();
    private List<DriveLogEntry> _entries = new();

    public IReadOnlyList<DriveLogEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(JsonPath))
                {
                    _entries = new List<DriveLogEntry>();
                    return;
                }

                var json = File.ReadAllText(JsonPath);
                _entries = JsonSerializer.Deserialize<List<DriveLogEntry>>(json, JsonOptions)
                           ?? new List<DriveLogEntry>();
                Logger.Info($"Drive log loaded: {_entries.Count} entries.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load drive log", ex);
                _entries = new List<DriveLogEntry>();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var json = JsonSerializer.Serialize(_entries, JsonOptions);
                var tmpPath = JsonPath + ".tmp";
                File.WriteAllText(tmpPath, json);

                // Backup existing file
                if (File.Exists(JsonPath))
                {
                    var bakPath = JsonPath + ".bak";
                    File.Copy(JsonPath, bakPath, true);
                }

                File.Move(tmpPath, JsonPath, true);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save drive log", ex);
            }
        }
    }

    public DriveLogEntry? GetEntry(string serialNumber)
    {
        lock (_lock)
            return _entries.FirstOrDefault(e =>
                string.Equals(e.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
    }

    public DriveLogEntry GetOrCreateEntry(PhysicalDrive drive)
    {
        if (string.IsNullOrWhiteSpace(drive.SerialNumber))
        {
            Logger.Warning($"Drive Disk {drive.DeviceNumber} has no serial number — skipping drive log.");
            return null!;
        }

        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.SerialNumber, drive.SerialNumber, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                entry = new DriveLogEntry
                {
                    SerialNumber = drive.SerialNumber,
                    FirstSeenUtc = DateTime.UtcNow
                };
                _entries.Add(entry);
            }

            // Update identity fields (may change if firmware updated, etc.)
            entry.Model = drive.Model;
            entry.Manufacturer = drive.Manufacturer;
            entry.SizeBytes = drive.SizeBytes;
            entry.MediaType = drive.MediaType;
            entry.InterfaceType = drive.InterfaceType;
            entry.FirmwareVersion = drive.FirmwareVersion;
            entry.PartNumber = drive.PartNumber;
            entry.LastSeenUtc = DateTime.UtcNow;

            return entry;
        }
    }

    public void RecordSurfaceCheck(PhysicalDrive drive, SurfaceCheckReport report)
    {
        var entry = GetOrCreateEntry(drive);
        if (entry == null) return;

        lock (_lock)
        {
            entry.Operations.Add(new LogOperationRecord
            {
                Type = OperationType.SurfaceCheck,
                StartUtc = report.StartTime.ToUniversalTime(),
                EndUtc = report.EndTime.ToUniversalTime(),
                Passed = report.Passed,
                Summary = report.Passed ? "Healthy — no read errors" : $"{report.ReadErrors} read error(s)",
                SectorsSampled = report.TotalSectorsSampled,
                CoveragePercent = Math.Round(report.CoveragePercent, 2),
                ReadErrors = report.ReadErrors,
                DataPresencePercent = Math.Round(report.DataPresencePercent, 2)
            });
            entry.LastOperationUtc = DateTime.UtcNow;
        }

        UpdateHealthSnapshot(drive);
        Save();
    }

    public void RecordWipe(PhysicalDrive drive, WipeReport report)
    {
        var entry = GetOrCreateEntry(drive);
        if (entry == null) return;

        lock (_lock)
        {
            entry.Operations.Add(new LogOperationRecord
            {
                Type = OperationType.SecureWipe,
                StartUtc = report.StartTime.ToUniversalTime(),
                EndUtc = report.EndTime.ToUniversalTime(),
                Passed = report.Completed && report.WriteErrors == 0,
                Summary = report.Completed
                    ? (report.WriteErrors == 0 ? "Wipe complete — no errors" : $"Wipe complete — {report.WriteErrors} write error(s)")
                    : "Wipe incomplete",
                SectorsWritten = report.SectorsWritten,
                WriteErrors = report.WriteErrors,
                WipeMethod = report.Method,
                WipeMode = report.Mode
            });
            entry.LastOperationUtc = DateTime.UtcNow;
        }

        Save();
    }

    public void UpdateHealthSnapshot(PhysicalDrive drive)
    {
        if (drive.Health == null) return;
        var entry = GetOrCreateEntry(drive);
        if (entry == null) return;

        lock (_lock)
        {
            entry.HealthSnapshot = new LogHealthSnapshot
            {
                HealthStatus = drive.Health.HealthStatus,
                RiskLevel = drive.Health.RiskLevel,
                Temperature = drive.Health.Temperature,
                PowerOnHours = drive.Health.PowerOnHours,
                ReadErrors = drive.Health.ReadErrors,
                WriteErrors = drive.Health.WriteErrors,
                Wear = drive.Health.Wear,
                CapturedUtc = DateTime.UtcNow
            };
        }

        Save();
    }

    public void UpdateStatus(string serialNumber, DriveLogStatus status)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
                entry.Status = status;
        }
        Save();
    }

    public void AddNote(string serialNumber, string text)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
            entry?.Notes.Add(new LogNote
            {
                TimestampUtc = DateTime.UtcNow,
                Text = text
            });
        }
        Save();
    }

    public void RemoveNote(string serialNumber, LogNote note)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
            entry?.Notes.Remove(note);
        }
        Save();
    }

    // ── Photo Management ──

    public string GetPhotoDirectory(string serialNumber)
    {
        var dir = Path.Combine(PhotosRoot, SanitizeFileName(serialNumber));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string AddPhoto(string serialNumber, string sourceFilePath)
    {
        var dir = GetPhotoDirectory(serialNumber);
        var fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Path.GetFileName(sourceFilePath)}";
        var destPath = Path.Combine(dir, fileName);
        File.Copy(sourceFilePath, destPath, true);

        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
            if (entry != null && !entry.Photos.Contains(fileName))
                entry.Photos.Add(fileName);
        }
        Save();
        return destPath;
    }

    public void RemovePhoto(string serialNumber, string fileName)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
            entry?.Photos.Remove(fileName);
        }

        try
        {
            var filePath = Path.Combine(GetPhotoDirectory(serialNumber), fileName);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to delete photo file: {ex.Message}");
        }

        Save();
    }

    public string GetPhotoFullPath(string serialNumber, string fileName)
        => Path.Combine(GetPhotoDirectory(serialNumber), fileName);

    // ── Export ──

    public void Export(string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);

        // Export JSON
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(Path.Combine(destinationFolder, "drive-log.json"), json);
        }

        // Export photos
        if (Directory.Exists(PhotosRoot))
        {
            var destPhotos = Path.Combine(destinationFolder, "photos");
            CopyDirectory(PhotosRoot, destPhotos);
        }
    }

    public void DeleteEntry(string serialNumber)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e =>
                string.Equals(e.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
        }

        // Clean up photos
        try
        {
            var photoDir = Path.Combine(PhotosRoot, SanitizeFileName(serialNumber));
            if (Directory.Exists(photoDir))
                Directory.Delete(photoDir, true);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to delete photo directory: {ex.Message}");
        }

        Save();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
