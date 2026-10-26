using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stalker.Gamma.Models;

namespace StalkerGamma.Gui.Services;

/// <summary>What a leftover resume.txt says it was written by.</summary>
public sealed record PendingResume(string Command, string Path);

/// <summary>
/// Mirror of the CLI's ResumeUtility: a failed install/update leaves resume.txt in the GAMMA
/// folder and the next run with "resume" picks up where it stopped instead of re-downloading.
/// </summary>
public class ResumeService(LogService log)
{
    public const string FullInstallCommand = "full-install";
    public const string UpdateApplyCommand = "update apply";

    /// <summary>The resume file left behind in <paramref name="gamma"/>, or null if there is none.</summary>
    public PendingResume? Find(string? gamma)
    {
        if (string.IsNullOrWhiteSpace(gamma))
        {
            return null;
        }
        try
        {
            var path = ResumeState.GetPath(gamma);
            if (!File.Exists(path))
            {
                return null;
            }
            var command = File.ReadLines(path)
                .FirstOrDefault(x => x.StartsWith("# command: ", StringComparison.Ordinal))
                ?["# command: ".Length..]
                .Trim();
            return command is { Length: > 0 } ? new PendingResume(command, path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Loads the previous run's state when <paramref name="resume"/> is set, else starts fresh.</summary>
    public async Task<ResumeState> LoadAsync(
        bool resume,
        string gamma,
        string command,
        CancellationToken ct
    )
    {
        var path = ResumeState.GetPath(gamma);
        if (!resume)
        {
            return new ResumeState { Command = command, FilePath = path };
        }
        if (!File.Exists(path))
        {
            log.Append($"No resume file found at {path}; starting from scratch.");
            return new ResumeState { Command = command, FilePath = path };
        }
        var state = await ResumeState.ReadAsync(path, ct);
        if (state.Command != command)
        {
            throw new ResumeStateException(
                $"The resume file was left by '{state.Command}', not '{command}'. Resume that one, or discard the resume file."
            );
        }
        log.Append($"Resuming from {path}");
        state.FilePath = path;
        return state;
    }

    /// <summary>Persist progress so the next run can continue after a failure.</summary>
    public void Write(ResumeState state, string gamma)
    {
        if (state.IsEmpty)
        {
            return;
        }
        var path = ResumeState.GetPath(gamma);
        try
        {
            state.Write(path);
            log.Append($"Progress saved to {path} — use Resume to continue where this left off.");
        }
        catch (Exception e)
        {
            log.Append($"Unable to write resume file {path}: {e.Message}");
        }
    }

    public void Delete(string gamma)
    {
        try
        {
            var path = ResumeState.GetPath(gamma);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Append($"Could not delete resume file: {e.Message}");
        }
    }
}
