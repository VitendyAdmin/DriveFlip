using System;
using System.IO;

namespace DriveFlip.Services;

public enum LogLevel { Trace, Debug, Info, Warn, Error, Fatal }

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _logPath;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Error;

    public static string LogFilePath => _logPath;

    public static string[] ReadTailLines(int count)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_logPath)) return [];
                var lines = File.ReadAllLines(_logPath);
                return lines.Length <= count ? lines : lines[^count..];
            }
            catch { return []; }
        }
    }

    static Logger()
    {
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DriveFlip");
        Directory.CreateDirectory(appDir);
        _logPath = Path.Combine(appDir, $"DriveFlip_Log_{DateTime.Now:yyyyMMdd}.txt");
    }

    public static void Trace(string message) => Write(LogLevel.Trace, message);
    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warning(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message) => Write(LogLevel.Error, message);
    public static void Error(string message, Exception ex) => Write(LogLevel.Error, FormatException(message, ex));
    public static void Fatal(string message) => Write(LogLevel.Fatal, message);
    public static void Fatal(string message, Exception ex) => Write(LogLevel.Fatal, FormatException(message, ex));

    private static string FormatException(string message, Exception ex)
        => $"{message} — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";

    private static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] {message}";
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch
            {
                // Logging should never crash the app
            }
        }
    }
}
