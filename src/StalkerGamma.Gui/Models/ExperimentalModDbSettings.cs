using Stalker.Gamma.Models;

namespace StalkerGamma.Gui.Models;

/// <summary>Vendored from stalker-gamma-cli/Models/ExperimentalModDbSettings.cs.</summary>
public class ExperimentalModDbSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public ushort Port { get; set; } = 8000;

    public ExperimentalPythonServerSettings ToServerSettings() =>
        new() { Host = Host, Port = Port };
}
