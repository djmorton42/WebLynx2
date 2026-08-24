using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebLynx2.Models;

namespace WebLynx2.Api;

public sealed class RaceHttpServer(
    ILogger<RaceHttpServer> logger,
    RaceStateManager raceState,
    KeyValueStoreService keyValueStore,
    int delayedDisplaySeconds) : IAsyncDisposable
{
    private readonly RaceDataApiMapper _mapper = new(keyValueStore, delayedDisplaySeconds);
    private readonly object _gate = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _listener?.IsListening == true;
        }
    }

    public Task StartAsync(int port)
    {
        lock (_gate)
        {
            if (_listener?.IsListening == true)
                throw new InvalidOperationException("Race HTTP server is already running.");

            _listener = new HttpListener();
            AddPrefixes(_listener, port);
            _listener.Start();

            _cts = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync(_cts.Token);
        }

        logger.LogInformation("Race HTTP server listening on port {Port}", port);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? acceptLoop;
        CancellationTokenSource? cts;
        HttpListener? listener;

        lock (_gate)
        {
            acceptLoop = _acceptLoop;
            cts = _cts;
            listener = _listener;

            _acceptLoop = null;
            _cts = null;
            _listener = null;
        }

        if (listener is null)
            return;

        cts?.Cancel();

        try
        {
            listener.Stop();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode is 995 or 500)
        {
            // Listener already stopped during shutdown.
        }

        listener.Close();

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cts?.Dispose();
        logger.LogInformation("Race HTTP server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static void AddPrefixes(HttpListener listener, int port)
    {
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");

        if (OperatingSystem.IsWindows())
            listener.Prefixes.Add($"http://+:{port}/");
        else
            listener.Prefixes.Add($"http://*:{port}/");
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListener? listener;
            lock (_gate)
                listener = _listener;

            if (listener is null || !listener.IsListening)
                break;

            try
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException ex)
            {
                logger.LogError(ex, "HTTP listener error");
                break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var path = NormalizePath(request.Url?.AbsolutePath);

            if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteNotFoundAsync(context.Response).ConfigureAwait(false);
                return;
            }

            if (path.Equals("/api/race/race-data", StringComparison.OrdinalIgnoreCase))
            {
                var sortBy = request.QueryString["sortBy"] ?? "place";
                var apiResponse = _mapper.Map(raceState.GetCurrentRaceState(), sortBy);
                await WriteJsonAsync(context.Response, apiResponse, HttpStatusCode.OK).ConfigureAwait(false);
                return;
            }

            if (path.Equals("/api/race/current", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, raceState.GetCurrentRaceState(), HttpStatusCode.OK)
                    .ConfigureAwait(false);
                return;
            }

            await WriteNotFoundAsync(context.Response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling HTTP request");
            await WriteInternalServerErrorAsync(context.Response).ConfigureAwait(false);
        }
    }

    private static string NormalizePath(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return "/";

        var path = absolutePath.TrimEnd('/');
        return string.IsNullOrEmpty(path) ? "/" : path;
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, HttpStatusCode statusCode)
    {
        var json = JsonSerializer.Serialize(payload, RaceHttpJsonSerializer.Options);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;

        try
        {
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }

    private static Task WriteNotFoundAsync(HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.NotFound;
        response.Close();
        return Task.CompletedTask;
    }

    private static async Task WriteInternalServerErrorAsync(HttpListenerResponse response)
    {
        const string message = "Internal server error";
        var bytes = Encoding.UTF8.GetBytes(message);

        response.StatusCode = (int)HttpStatusCode.InternalServerError;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;

        try
        {
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }
}
