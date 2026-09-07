using System.Collections.Specialized;
using System.Net;
using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.UnofficialResults;
using WebLynx2.Utilities;

namespace WebLynx2.Api;

public sealed class RaceHttpServer : IAsyncDisposable
{
    private readonly ILogger<RaceHttpServer> _logger;
    private readonly RaceHttpApp _app;
    private readonly object _gate = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public RaceHttpServer(
        ILogger<RaceHttpServer> logger,
        RaceStateManager raceState,
        KeyValueStoreService keyValueStore,
        int delayedDisplaySeconds,
        string? viewsRootPath = null,
        UnofficialResultsCatalog? unofficialResults = null,
        AnnouncementOverrideService? announcementOverride = null)
    {
        _logger = logger;
        _app = new RaceHttpApp(
            logger,
            raceState,
            keyValueStore,
            delayedDisplaySeconds,
            viewsRootPath,
            unofficialResults,
            announcementOverride);
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _listener?.IsListening == true;
        }
    }

    /// <param name="port">TCP port to listen on.</param>
    /// <param name="listenAddress">
    /// IPv4 address to bind, or null/empty/"*" for all interfaces.
    /// </param>
    public Task StartAsync(int port, string? listenAddress = null)
    {
        IReadOnlyList<string> prefixes;
        lock (_gate)
        {
            if (_listener?.IsListening == true)
                throw new InvalidOperationException("Race HTTP server is already running.");

            prefixes = NetworkAddressHelper.GetHttpListenerPrefixes(port, listenAddress);
            _listener = new HttpListener();
            foreach (var prefix in prefixes)
                _listener.Prefixes.Add(prefix);
            _listener.Start();

            _cts = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync(_cts.Token);
        }

        _logger.LogInformation(
            "Race HTTP server listening on port {Port} ({Prefixes})",
            port,
            string.Join(", ", prefixes));
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
        _logger.LogInformation("Race HTTP server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

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
                _logger.LogError(ex, "HTTP listener error");
                break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var appRequest = ToAppRequest(context.Request);
            var appResponse = await _app.HandleAsync(appRequest).ConfigureAwait(false);
            await WriteResponseAsync(context.Response, appResponse).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling HTTP request");
            try
            {
                await WriteResponseAsync(context.Response, RaceHttpResponse.InternalServerError())
                    .ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static RaceHttpRequest ToAppRequest(HttpListenerRequest request) =>
        new()
        {
            Method = request.HttpMethod,
            Path = request.Url?.AbsolutePath ?? "/",
            Query = ToQueryDictionary(request.QueryString)
        };

    private static Dictionary<string, string> ToQueryDictionary(NameValueCollection query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in query.AllKeys)
        {
            if (key is null || result.ContainsKey(key))
                continue;

            result[key] = query[key] ?? "";
        }

        return result;
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, RaceHttpResponse appResponse)
    {
        response.StatusCode = (int)appResponse.StatusCode;

        if (appResponse.Location is not null)
            response.RedirectLocation = appResponse.Location;

        if (appResponse.ContentType is not null)
            response.ContentType = appResponse.ContentType;

        if (appResponse.CacheControl is not null)
            response.Headers["Cache-Control"] = appResponse.CacheControl;

        try
        {
            if (appResponse.Body.Length > 0)
            {
                response.ContentLength64 = appResponse.Body.Length;
                await response.OutputStream.WriteAsync(appResponse.Body).ConfigureAwait(false);
            }
        }
        finally
        {
            response.Close();
        }
    }
}
