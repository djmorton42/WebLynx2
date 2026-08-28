using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebLynx2.UnofficialResults;

/// <summary>
/// Periodically refreshes an <see cref="UnofficialResultsCatalog"/> from a configured directory.
/// </summary>
public sealed class UnofficialResultsPoller : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public UnofficialResultsPoller(
        UnofficialResultsCatalog catalog,
        ILogger? logger = null)
    {
        Catalog = catalog;
        _logger = logger ?? NullLogger.Instance;
    }

    public UnofficialResultsCatalog Catalog { get; }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _loop is { IsCompleted: false };
        }
    }

    public void Start(string directoryPath, TimeSpan pollingInterval)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            _logger.LogWarning("Unofficial results path is empty; poller will not start.");
            return;
        }

        lock (_gate)
        {
            if (_loop is { IsCompleted: false })
                throw new InvalidOperationException("Unofficial results poller is already running.");

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = RunAsync(directoryPath, pollingInterval, token);
        }

        _logger.LogInformation(
            "Unofficial results poller started for {Path} every {Seconds}s",
            directoryPath,
            pollingInterval.TotalSeconds);
    }

    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
        }

        if (cts is null)
            return;

        cts.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts.Dispose();
        _logger.LogInformation("Unofficial results poller stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task RunAsync(string directoryPath, TimeSpan pollingInterval, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Catalog.RefreshAsync(directoryPath, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling unofficial results directory");
            }

            try
            {
                await Task.Delay(pollingInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
