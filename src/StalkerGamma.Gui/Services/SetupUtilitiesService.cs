using System;
using System.IO;
using Stalker.Gamma.Models;

namespace StalkerGamma.Gui.Services;

/// <summary>
/// Vendored from stalker-gamma-cli/Services/SetupUtilitiesService.cs: points the engine at the
/// bundled binaries next to the executable (resources/7zz, resources/cloudscraper).
/// </summary>
public class SetupUtilitiesService(StalkerGammaSettings settings)
{
    public void Setup()
    {
        settings.PathTo7Z = Path.Join(ResourcesPath, OperatingSystem.IsWindows() ? "7zz.exe" : "7zz");
        settings.PythonServerPath = Path.Join(
            ResourcesPath,
            OperatingSystem.IsWindows() ? "cloudscraper.exe" : "cloudscraper"
        );
    }

    private static readonly string ResourcesPath = Path.Join(
        Path.GetDirectoryName(AppContext.BaseDirectory),
        "resources"
    );
}
