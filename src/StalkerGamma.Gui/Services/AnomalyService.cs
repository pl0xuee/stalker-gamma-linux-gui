using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Stalker.Gamma.Utilities;

namespace StalkerGamma.Gui.Services;

public sealed record AnomalyCheckResult(int Ok, List<string> Corrupt, List<string> Missing);

/// <summary>
/// Ported from stalker-gamma-cli/Commands/Anomaly.cs: verify an Anomaly install against
/// tools/checksums.md5, purge shader cache, delete ReShade leftovers.
/// </summary>
public class AnomalyService
{
    public async Task<AnomalyCheckResult> CheckAsync(
        string anomaly,
        Action<double>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Directory.Exists(anomaly))
        {
            throw new DirectoryNotFoundException($"Directory {anomaly} doesn't exist");
        }
        var checksumsPath = Path.Join(anomaly, "tools", "checksums.md5");
        if (!File.Exists(checksumsPath))
        {
            throw new FileNotFoundException($"File {checksumsPath} doesn't exist");
        }

        var checksums = (await File.ReadAllTextAsync(checksumsPath, cancellationToken))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var split = line.Split(
                    ' ',
                    2,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );
                return (
                    Md5: split[0],
                    Path: Path.GetFullPath(
                        split[1]
                            .Replace("*", $"{anomaly}{Path.DirectorySeparatorChar}")
                            .Replace('\\', Path.DirectorySeparatorChar)
                    )
                );
            })
            .ToList();

        var ok = 0;
        var corrupt = new List<string>();
        var missing = new List<string>();
        var done = 0;
        foreach (var (md5, path) in checksums)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                missing.Add(path);
            }
            else
            {
                var actual = await HashUtils.HashFile(
                    path,
                    HashAlgorithmName.MD5,
                    cancellationToken: cancellationToken
                );
                if (string.Equals(actual, md5, StringComparison.OrdinalIgnoreCase))
                {
                    ok++;
                }
                else
                {
                    corrupt.Add(path);
                }
            }
            onProgress?.Invoke((double)++done / checksums.Count);
        }
        return new AnomalyCheckResult(ok, corrupt, missing);
    }

    public bool PurgeShaderCache(string anomaly)
    {
        var shadersDir = Path.Join(anomaly, "appdata", "shaders_cache");
        if (!Directory.Exists(shadersDir))
        {
            return false;
        }
        DirUtils.NormalizePermissions(shadersDir);
        Directory.Delete(shadersDir, true);
        return true;
    }

    public List<string> DeleteReshade(string anomaly)
    {
        var deleted = new List<string>();
        var binDir = Path.Join(anomaly, "bin");
        List<string> reshadeFiles =
        [
            "d3d9.dll",
            "dxgi.dll",
            "dxgi.log",
            "G.A.M.M.A.Reshade.ini",
            "ReShade.ini",
            "ReShade.log",
        ];
        if (!Directory.Exists(binDir))
        {
            return deleted;
        }
        foreach (
            var fi in new DirectoryInfo(binDir)
                .EnumerateFiles()
                .Where(x => reshadeFiles.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
        )
        {
            fi.Delete();
            deleted.Add(fi.Name);
        }
        var reshadeShadersDir = Path.Join(binDir, "reshade-shaders");
        if (Directory.Exists(reshadeShadersDir))
        {
            Directory.Delete(reshadeShadersDir, true);
            deleted.Add("reshade-shaders/");
        }
        return deleted;
    }
}
