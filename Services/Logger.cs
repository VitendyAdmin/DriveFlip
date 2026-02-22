using System;
using System.IO;

namespace DriveFlip.Services;

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _logPath;

    static Logger()
    {
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DriveFlip");
        Directory.CreateDirectory(appDir);
        _logPath = Path.Combine(appDir, $"DriveFlip_Log_{DateTime.Now:yyyyMMdd}.txt");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warning(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex)
        => Write("ERROR", $"{message} — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
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
