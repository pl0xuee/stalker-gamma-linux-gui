using System;

namespace StalkerGamma.Gui.Services;

/// <summary>In-app log sink feeding the collapsible log pane. Thread-safe; UI marshalling is the subscriber's job.</summary>
public class LogService
{
    public event Action<string>? LineAdded;

    public void Append(string line) => LineAdded?.Invoke($"[{DateTime.Now:HH:mm:ss}] {line}");
}
