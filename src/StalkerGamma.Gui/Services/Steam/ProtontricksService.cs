using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StalkerGamma.Gui.Services.Steam;

/// <summary>
/// Runs protontricks to install prefix prerequisites (Jackify protontricks_prefix port).
/// GAMMA verb set comes from the community Linux guide; dxvk is omitted because Proton
/// ships its own.
/// </summary>
public class ProtontricksService(LogService log)
{
    public static readonly string[] GammaComponents =
    [
        "d3dcompiler_47",
        "d3dx10",
        "d3dx11_43",
        "d3dx9",
        "dx8vb",
        "quartz",
        "vcrun2022",
    ];

    public bool IsAvailable(out string version)
    {
        version = "";
        try
        {
            var (code, stdout, _) = Run("protontricks", ["-V"], TimeSpan.FromSeconds(20));
            if (code != 0)
            {
                return false;
            }
            version = stdout.Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task InstallComponentsAsync(long unsignedAppId, CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            log.Append(
                $"protontricks attempt {attempt}/3: installing {string.Join(" ", GammaComponents)}"
            );
            var (code, stdout, stderr) = await RunAsync(
                "protontricks",
                ["--no-bwrap", unsignedAppId.ToString(), "-q", .. GammaComponents],
                TimeSpan.FromMinutes(15),
                ct
            );
            LogTail(stdout, stderr);
            if (code == 0 || await VerifyAsync(unsignedAppId, ct))
            {
                log.Append("Components installed");
                await SetWin10Async(unsignedAppId, ct);
                return;
            }
            log.Append($"protontricks exited with {code}; cleaning up wine processes before retry");
            Cleanup();
        }
        throw new InvalidOperationException("protontricks failed after 3 attempts — see log");
    }

    public async Task<bool> VerifyAsync(long unsignedAppId, CancellationToken ct = default)
    {
        var (code, stdout, _) = await RunAsync(
            "protontricks",
            ["--no-bwrap", unsignedAppId.ToString(), "list-installed"],
            TimeSpan.FromMinutes(2),
            ct
        );
        if (code != 0)
        {
            return false;
        }
        // vcrun2022 shows up as vcrun2022; the d3d/x components appear verbatim.
        return GammaComponents.All(c => stdout.Contains(c, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SetWin10Async(long unsignedAppId, CancellationToken ct)
    {
        log.Append("Setting Windows 10 mode");
        var (code, _, stderr) = await RunAsync(
            "protontricks",
            ["--no-bwrap", unsignedAppId.ToString(), "win10"],
            TimeSpan.FromMinutes(3),
            ct
        );
        if (code != 0)
        {
            log.Append($"win10 mode failed (non-fatal): {Tail(stderr)}");
        }
    }

    private void Cleanup()
    {
        try
        {
            Run("wineserver", ["-k"], TimeSpan.FromSeconds(15));
        }
        catch
        {
            // wineserver may not be on PATH; best-effort cleanup only
        }
    }

    private static (int Code, string StdOut, string StdErr) Run(
        string file,
        string[] args,
        TimeSpan timeout
    )
    {
        var psi = BuildPsi(file, args);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(timeout))
        {
            p.Kill(true);
            throw new TimeoutException($"{file} timed out after {timeout}");
        }
        return (p.ExitCode, stdout, stderr);
    }

    private static async Task<(int Code, string StdOut, string StdErr)> RunAsync(
        string file,
        string[] args,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        var psi = BuildPsi(file, args);
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            p.Kill(true);
            if (ct.IsCancellationRequested)
            {
                throw;
            }
            throw new TimeoutException($"{file} timed out after {timeout}");
        }
        return (p.ExitCode, await stdoutTask, await stderrTask);
    }

    private static ProcessStartInfo BuildPsi(string file, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        psi.Environment["WINEDEBUG"] = "-all";
        psi.Environment["WINETRICKS_SUPER_QUIET"] = "1";
        return psi;
    }

    private void LogTail(string stdout, string stderr)
    {
        var tail = Tail(stdout + "\n" + stderr);
        if (!string.IsNullOrWhiteSpace(tail))
        {
            log.Append(tail);
        }
    }

    private static string Tail(string s, int lines = 8) =>
        string.Join(
            '\n',
            s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .TakeLast(lines)
        );
}
