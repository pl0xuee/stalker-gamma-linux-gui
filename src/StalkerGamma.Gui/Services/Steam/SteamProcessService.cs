using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace StalkerGamma.Gui.Services.Steam;

/// <summary>
/// Shuts down and restarts native Steam (Jackify steam_restart_service port).
/// Jackify deliberately avoids `steam -shutdown` as unreliable; we pkill and verify
/// steamwebhelper is gone, then relaunch detached.
/// </summary>
public class SteamProcessService(LogService log)
{
    public bool IsRunning() => Run("pgrep", "-f steamwebhelper").ExitCode == 0;

    /// <summary>
    /// The main client alone counts too: a Steam that is updating or whose webhelper crashed
    /// is still alive and will overwrite shortcuts.vdf/config.vdf when it saves or exits.
    /// </summary>
    private static bool AnyRunning() =>
        Run("pgrep", "-f steamwebhelper").ExitCode == 0 || Run("pgrep", "-x steam").ExitCode == 0;

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        if (!AnyRunning())
        {
            log.Append("Steam is not running");
            return;
        }
        log.Append("Shutting down Steam…");
        Run("pkill", "steam");
        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(1000, ct);
            if (!AnyRunning())
            {
                await WaitForFullExitAsync(ct);
                return;
            }
        }
        log.Append("Steam still running, escalating to SIGKILL");
        Run("pkill", "-9 steam");
        await Task.Delay(2000, ct);
        if (AnyRunning())
        {
            throw new InvalidOperationException("Could not stop Steam");
        }
        await WaitForFullExitAsync(ct);
    }

    /// <summary>
    /// steamwebhelper dies first, but the main steam process lingers several seconds
    /// finishing teardown (registry.vdf etc.). Relaunching in that window makes the new
    /// steam defer to the dying instance and exit, leaving nothing running.
    /// </summary>
    private async Task WaitForFullExitAsync(CancellationToken ct)
    {
        for (var i = 0; i < 30; i++)
        {
            if (Run("pgrep", "-x steam").ExitCode != 0)
            {
                log.Append("Steam stopped");
                return;
            }
            await Task.Delay(1000, ct);
        }
        // Writing the VDFs now would race the dying client, which overwrites them on exit.
        throw new InvalidOperationException("Steam did not finish shutting down within 30s");
    }

    public async Task StartAndWaitAsync(CancellationToken ct = default)
    {
        log.Append("Starting Steam…");
        // Detach fully so Steam survives the GUI closing; inherit the desktop environment.
        Process.Start(
            new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = "-c \"setsid steam -foreground >/dev/null 2>&1 &\"",
                UseShellExecute = false,
            }
        );

        // Wait for steamwebhelper to be up and stable (Jackify: >=20s stable, 180s timeout).
        var stableSince = (DateTime?)null;
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000, ct);
            if (IsRunning())
            {
                stableSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - stableSince >= TimeSpan.FromSeconds(20))
                {
                    log.Append("Steam is running");
                    return;
                }
            }
            else
            {
                stableSince = null;
            }
        }
        throw new TimeoutException("Steam did not come back up within 180s");
    }

    private static (int ExitCode, string StdOut) Run(string file, string args)
    {
        using var p = Process.Start(
            new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        )!;
        // Drain both pipes concurrently or a chatty child can fill one and deadlock us.
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.GetAwaiter().GetResult());
    }
}
