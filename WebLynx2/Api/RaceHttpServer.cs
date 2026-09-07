using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

    private WebApplication? _webApp;

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
                return _webApp is not null;
        }
    }

    /// <param name="port">TCP port to listen on.</param>
    /// <param name="listenAddress">
    /// IPv4 address to bind, or null/empty/"*" for all interfaces (0.0.0.0).
    /// </param>
    public async Task StartAsync(int port, string? listenAddress = null)
    {
        var url = NetworkAddressHelper.GetKestrelUrl(port, listenAddress);

        WebApplication webApp;
        lock (_gate)
        {
            if (_webApp is not null)
                throw new InvalidOperationException("Race HTTP server is already running.");

            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(RaceHttpServer).Assembly.GetName().Name
            });
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls(url);
            builder.Logging.ClearProviders();

            webApp = builder.Build();
            webApp.Run(HandleHttpContextAsync);
            _webApp = webApp;
        }

        try
        {
            await webApp.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_webApp, webApp))
                    _webApp = null;
            }

            await webApp.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation("Race HTTP server listening on {Url}", url);
    }

    public async Task StopAsync()
    {
        WebApplication? webApp;
        lock (_gate)
        {
            webApp = _webApp;
            _webApp = null;
        }

        if (webApp is null)
            return;

        try
        {
            await webApp.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await webApp.DisposeAsync().ConfigureAwait(false);
        }

        _logger.LogInformation("Race HTTP server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task HandleHttpContextAsync(HttpContext context)
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
            catch (ObjectDisposedException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static RaceHttpRequest ToAppRequest(HttpRequest request)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in request.Query)
        {
            if (query.ContainsKey(pair.Key))
                continue;

            query[pair.Key] = pair.Value.ToString();
        }

        return new RaceHttpRequest
        {
            Method = request.Method,
            Path = request.Path.HasValue ? request.Path.Value! : "/",
            Query = query
        };
    }

    private static async Task WriteResponseAsync(HttpResponse response, RaceHttpResponse appResponse)
    {
        response.StatusCode = (int)appResponse.StatusCode;

        if (appResponse.Location is not null)
            response.Headers.Location = appResponse.Location;

        if (appResponse.ContentType is not null)
            response.ContentType = appResponse.ContentType;

        if (appResponse.CacheControl is not null)
            response.Headers.CacheControl = appResponse.CacheControl;

        if (appResponse.Body.Length > 0)
            await response.Body.WriteAsync(appResponse.Body).ConfigureAwait(false);
    }
}
