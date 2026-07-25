using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StalkerGamma.Gui.Utilities;

namespace StalkerGamma.Gui.Services;

public sealed record UltrawideFixResult(bool Ok, string Message);

/// <summary>
/// Un-stretches the in-game UI on ultrawide monitors.
///
/// GAMMA already downloads everything needed — it just ships it switched off in modlist.txt:
///   449- Dynamic Aspect Ratio Tweaks (DART, bellyillish) — HUD, 3D PDA, minimap, map markers
///   258- 2D Scopes 21-9 Resolution Patch (Kuez)          — scope reticles, one per aspect ratio
///   259- 2D Scopes 16-10 Resolution Patch (Kuez)
///   260- 2D Scopes 32-9 Resolution Patch (Kuez)
/// So the fix is a modlist edit, not a download: enable DART plus the scope patch that matches
/// the monitor, and make sure the other two scope patches are off (they are mutually exclusive).
/// Everything is reversible from the Mods page.
/// </summary>
public class UltrawideFixService(LogService log)
{
    /// <summary>Aspect ratios GAMMA ships a 2D-scope patch for, widest first.</summary>
    public static readonly IReadOnlyList<string> AspectRatios = ["21:9", "32:9", "16:10"];

    public async Task<UltrawideFixResult> ApplyAsync(
        string gamma,
        string mo2Profile,
        string aspectRatio
    )
    {
        var modListPath = Path.Join(gamma, "profiles", mo2Profile, "modlist.txt");
        if (!File.Exists(modListPath))
        {
            return new UltrawideFixResult(
                false,
                $"modlist.txt not found ({modListPath}) — install GAMMA first."
            );
        }

        var mods = await ModListUtility.GetModListAsync(modListPath);
        var dart = mods.FirstOrDefault(m =>
            m.Status != ModStatus.Passthrough && Matches(m.Name, DartMarker)
        );
        if (dart is null)
        {
            return new UltrawideFixResult(
                false,
                "This modlist has no 'Dynamic Aspect Ratio Tweaks' entry — update GAMMA, then try again."
            );
        }

        var wantedScopeToken = ScopeToken(aspectRatio);
        var scopePatches = mods
            .Where(m => m.Status != ModStatus.Passthrough && IsScopeAspectPatch(m.Name))
            .ToList();

        var enabled = new List<string>();
        var alreadyOn = new List<string>();
        var turnedOff = new List<string>();

        Enable(dart);
        foreach (var patch in scopePatches)
        {
            if (Matches(patch.Name, wantedScopeToken))
            {
                Enable(patch);
            }
            else if (patch.Status == ModStatus.Enabled)
            {
                // Only one 2D-scope resolution patch may be active at a time.
                patch.Status = ModStatus.Disabled;
                turnedOff.Add(patch.Name);
            }
        }

        if (enabled.Count == 0 && turnedOff.Count == 0)
        {
            return new UltrawideFixResult(
                true,
                $"Already applied for {aspectRatio}: {string.Join(", ", alreadyOn)}. "
                    + "Restart the game if it is running."
            );
        }

        await ModListUtility.SaveModListAsync(modListPath, mods);

        foreach (var name in enabled)
        {
            log.Append($"Ultrawide fix: enabled '{name}'");
        }
        foreach (var name in turnedOff)
        {
            log.Append($"Ultrawide fix: disabled '{name}' (conflicts with {aspectRatio})");
        }

        var missing = enabled
            .Concat(alreadyOn)
            .Where(name => !Directory.Exists(Path.Join(gamma, "mods", name)))
            .ToList();
        foreach (var name in missing)
        {
            log.Append($"Ultrawide fix: WARNING — no mod folder for '{name}'; run a full install.");
        }

        var summary =
            $"{aspectRatio}: enabled {enabled.Count + alreadyOn.Count} mod(s)"
            + (turnedOff.Count > 0 ? $", disabled {turnedOff.Count} conflicting" : "")
            + ". Launch the game and tune it under MCM → DART.";
        if (missing.Count > 0)
        {
            summary +=
                $" WARNING: {missing.Count} mod folder(s) missing — run a full install/update first.";
        }
        if (!scopePatches.Any(m => Matches(m.Name, wantedScopeToken)))
        {
            summary += $" (No 2D-scope patch for {aspectRatio} in this modlist — HUD fix only.)";
        }
        return new UltrawideFixResult(true, summary);

        void Enable(ModListRecord mod)
        {
            if (mod.Status == ModStatus.Enabled)
            {
                alreadyOn.Add(mod.Name);
                return;
            }
            mod.Status = ModStatus.Enabled;
            enabled.Add(mod.Name);
        }
    }

    /// <summary>GAMMA renumbers entries between releases, so match on the descriptive text.</summary>
    private const string DartMarker = "Dynamic Aspect Ratio Tweaks";

    /// <summary>
    /// Only the per-aspect-ratio reticle patches ("2D Scopes 21-9 Resolution Patch - Kuez"),
    /// never the scope mods themselves — "100- BAS Scopes Extravaganza 2d scopes" is enabled by
    /// default and switching it off would leave the player with no 2D scopes at all.
    /// </summary>
    private static bool IsScopeAspectPatch(string name) =>
        Matches(name, "Resolution Patch") && ScopeTokens.Any(t => Matches(name, t));

    private static readonly string[] ScopeTokens = ["21-9", "32-9", "16-10"];

    private static string ScopeToken(string aspectRatio) =>
        aspectRatio switch
        {
            "32:9" => "32-9",
            "16:10" => "16-10",
            _ => "21-9",
        };

    private static bool Matches(string name, string marker) =>
        name.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
