using System;
using System.IO;
using StalkerGamma.Gui.Models;

namespace StalkerGamma.Gui.Services;

/// <summary>
/// In-app log sink feeding the collapsible log pane, teed to
/// ~/.config/stalker-gamma/logs/stalker-gamma-gui.log so failed runs survive an app close.
/// Thread-safe; UI marshalling is the subscriber's job.
/// </summary>
public class LogService
{
    public event Action<string>? LineAdded;

    /// <summary>Null when the log directory could not be created; shown on the Settings page.</summary>
    public string? LogFilePath { get; }

    public LogService()
    {
        try
        {
            var logDir = Path.Join(CliSettings.AppDataPath, "logs");
            Directory.CreateDirectory(logDir);
            LogFilePath = Path.Join(logDir, "stalker-gamma-gui.log");
        }
        catch
        {
            LogFilePath = null;
        }
    }

    public void Append(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        LineAdded?.Invoke(stamped);
        if (LogFilePath is null)
        {
            return;
        }
        lock (_fileLock)
        {
            try
            {
                // A full install emits a debug line per file; without rotation the log grows
                // without bound across runs.
                Rotate();
                File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd}]{stamped}\n");
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }

    /// <summary>Caller must hold <see cref="_fileLock"/>.</summary>
    private void Rotate()
    {
        if (++_writesSinceSizeCheck < 500)
        {
            return;
        }
        _writesSinceSizeCheck = 0;
        var info = new FileInfo(LogFilePath!);
        if (info.Exists && info.Length > MaxLogBytes)
        {
            File.Move(LogFilePath!, LogFilePath + ".1", overwrite: true);
        }
    }

    private const long MaxLogBytes = 8L * 1024 * 1024;

    private readonly object _fileLock = new();
    private int _writesSinceSizeCheck = 500;
}
