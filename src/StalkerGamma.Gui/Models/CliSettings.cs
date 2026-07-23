using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StalkerGamma.Gui.Models;

/// <summary>
/// Vendored from stalker-gamma-cli/Models/CliSettings.cs — same JSON shape and path
/// (~/.config/stalker-gamma/settings.json) so the CLI and GUI stay interchangeable.
/// </summary>
public class CliSettings
{
    [JsonIgnore]
    public CliProfile? ActiveProfile => Profiles.FirstOrDefault(x => x.Active);

    public List<CliProfile> Profiles { get; set; } = [];

    public ExperimentalModDbSettings ExperimentalModDbSettings { get; set; } = new();

    public async Task<string?> SaveAsync()
    {
        if (!Directory.Exists(AppDataPath))
        {
            Directory.CreateDirectory(AppDataPath);
        }
        await Services.AtomicFile.WriteAllTextAsync(
            SettingsPath,
            JsonSerializer.Serialize(this, CliSettingsCtx.Default.CliSettings)
        );
        return ActiveProfile?.ProfileName;
    }

    public static CliSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new CliSettings();
        }
        try
        {
            return JsonSerializer.Deserialize(
                    File.ReadAllText(SettingsPath),
                    CliSettingsCtx.Default.CliSettings
                ) ?? new CliSettings();
        }
        catch (JsonException)
        {
            // A corrupt settings.json must not brick the app; keep the broken file for
            // inspection and start fresh in memory (only persisted if the user saves).
            File.Copy(SettingsPath, SettingsPath + ".corrupt", true);
            return new CliSettings();
        }
    }

    public static readonly string AppDataPath = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "stalker-gamma"
    );

    public static string SettingsPath => Path.Join(AppDataPath, "settings.json");
}

[JsonSerializable(typeof(CliSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class CliSettingsCtx : JsonSerializerContext;
