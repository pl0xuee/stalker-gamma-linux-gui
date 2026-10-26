using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace StalkerGamma.Gui.Services;

public enum OperationOutcome
{
    Succeeded,
    Cancelled,
    Failed,
}

public sealed record OperationResult(OperationOutcome Outcome, Exception? Error = null);

/// <summary>
/// Runs one engine operation at a time (the engine's GammaProgress/StalkerGammaSettings singletons
/// are not safe for concurrent operations). Each run gets a fresh DI scope, which also disposes
/// the scoped GammaInstaller (HttpClient) afterwards.
/// Observable so every page can grey out its action buttons while a run is in flight instead of
/// letting a second click fail with a confusing "Another operation is running".
/// </summary>
public class OperationRunner(
    IServiceProvider serviceProvider,
    SettingsService settingsService,
    LogService log,
    UtilitiesReadyService utilitiesReady,
    Stalker.Gamma.GammaInstallerServices.GammaProgress gammaProgress
) : ObservableObject
{
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? CurrentOperation { get; private set; }

    public event Action<string>? Started;
    public event Action<string, OperationResult>? Completed;

    public async Task<OperationResult> RunAsync(
        string name,
        Func<IServiceProvider, CancellationToken, Task> work,
        int? downloadThreadsOverride = null
    )
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return new OperationResult(
                OperationOutcome.Failed,
                new InvalidOperationException($"Another operation is running: {CurrentOperation}")
            );
        }

        CurrentOperation = name;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Started?.Invoke(name);
        log.Append($"{name}: started");
        OperationResult result;
        try
        {
            gammaProgress.Reset();
            // Before the dependency check: whether curl is needed depends on the server URL.
            settingsService.ApplyActiveProfileToEngine(downloadThreadsOverride);
            // Fail fast, not 30GB in: the CLI checks dependencies before every command.
            var (ready, reason) = utilitiesReady.Check();
            if (!ready)
            {
                throw new InvalidOperationException($"Missing dependencies:\n{reason}");
            }
            using var scope = serviceProvider.CreateScope();
            await Task.Run(() => work(scope.ServiceProvider, _cts.Token), _cts.Token);
            result = new OperationResult(OperationOutcome.Succeeded);
            log.Append($"{name}: finished");
        }
        catch (OperationCanceledException)
        {
            result = new OperationResult(OperationOutcome.Cancelled);
            log.Append($"{name}: cancelled");
        }
        catch (Exception) when (_cts?.IsCancellationRequested == true)
        {
            // The engine wraps cancellation in its own exception types ("Error extracting
            // repo" with a canceled inner task), so go by the token, not the exception.
            result = new OperationResult(OperationOutcome.Cancelled);
            log.Append($"{name}: cancelled");
        }
        catch (Exception e)
        {
            result = new OperationResult(OperationOutcome.Failed, e);
            log.Append($"{name}: FAILED — {e.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            CurrentOperation = null;
            Interlocked.Exchange(ref _busy, 0);
            IsBusy = false;
        }
        Completed?.Invoke(name, result);
        return result;
    }

    public void Cancel() => _cts?.Cancel();

    private int _busy;
    private bool _isBusy;
    private CancellationTokenSource? _cts;
}
