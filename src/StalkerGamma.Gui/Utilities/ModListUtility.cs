using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StalkerGamma.Gui.Utilities;

/// <summary>Vendored from stalker-gamma-cli/Utilities/ModListUtility.cs (MO2 modlist.txt parser).</summary>
public static class ModListUtility
{
    public static async Task<List<ModListRecord>> GetModListAsync(string pathToModList)
    {
        if (!File.Exists(pathToModList))
        {
            throw new FileNotFoundException($"File {pathToModList} doesn't exist");
        }

        return (await File.ReadAllLinesAsync(pathToModList))
            .Where(x => x.Length > 0 && x[0] != '#')
            .Select(x => x switch
            {
                ['+', .. var name] => new ModListRecord { Status = ModStatus.Enabled, Name = name },
                ['-', .. var name] => new ModListRecord { Status = ModStatus.Disabled, Name = name },
                // MO2 markers other than +/- (e.g. '*' unmanaged) must round-trip verbatim
                // and keep their position: modlist order is load order.
                _ => new ModListRecord { Status = ModStatus.Passthrough, Name = x, Raw = x },
            })
            .ToList();
    }

    public static async Task SaveModListAsync(string pathToModList, List<ModListRecord> mods) =>
        await AtomicWriteLinesAsync(
            pathToModList,
            mods.Select(x => x.Status switch
            {
                ModStatus.Passthrough => x.Raw ?? x.Name,
                ModStatus.Enabled => $"+{x.Name}",
                _ => $"-{x.Name}",
            })
        );

    private static async Task AtomicWriteLinesAsync(string path, System.Collections.Generic.IEnumerable<string> lines) =>
        await Services.AtomicFile.WriteAllTextAsync(path, string.Join('\n', lines) + '\n');
}

public enum ModStatus
{
    Enabled,
    Disabled,
    Passthrough,
}

public class ModListRecord
{
    public required ModStatus Status { get; set; }
    public required string Name { get; set; }
    public string? Raw { get; set; }
}
