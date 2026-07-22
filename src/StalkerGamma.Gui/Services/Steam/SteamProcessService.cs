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

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        if (!IsRunning())
        {
            log.Append("Steam is not running");
            return;
        }
        log.Append("Shutting down Steam…");
        Run("pkill", "steam");
        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(1000, ct);
            if (!IsRunning())
            {
                log.Append("Steam stopped");
                return;
            }
        }
        log.Append("Steam still running, escalating to SIGKILL");
        Run("pkill", "-9 steam");
        await Task.Delay(2000, ct);
        if (IsRunning())
        {
            throw new InvalidOperationException("Could not stop Steam");
        }
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
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout);
    }
}
