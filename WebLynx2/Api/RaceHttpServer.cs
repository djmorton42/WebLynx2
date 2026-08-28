using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.UnofficialResults;

namespace WebLynx2.Api;

public sealed class RaceHttpServer(
    ILogger<RaceHttpServer> logger,
    RaceStateManager raceState,
    KeyValueStoreService keyValueStore,
    int delayedDisplaySeconds,
    string? viewsRootPath = null,
    UnofficialResultsCatalog? unofficialResults = null) : IAsyncDisposable
{
    private readonly RaceDataApiMapper _mapper = new(keyValueStore, delayedDisplaySeconds);
    private readonly string? _viewsRoot = string.IsNullOrWhiteSpace(viewsRootPath)
        ? null
        : Path.GetFullPath(viewsRootPath);
    private readonly UnofficialResultsCatalog? _unofficialResults = unofficialResults;
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

            if (path.Equals("/", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Redirect;
                context.Response.RedirectLocation = "/views";
                context.Response.Close();
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

            if (await TryServeUnofficialResultsAsync(path, context.Response).ConfigureAwait(false))
                return;

            if (await TryServeViewsAsync(path, context.Response).ConfigureAwait(false))
                return;

            await WriteNotFoundAsync(context.Response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling HTTP request");
            await WriteInternalServerErrorAsync(context.Response).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryServeUnofficialResultsAsync(string path, HttpListenerResponse response)
    {
        if (!path.StartsWith("/api/unofficial_results", StringComparison.OrdinalIgnoreCase))
            return false;

        if (_unofficialResults is null)
        {
            await WritePlainAsync(response, "Unofficial results are not available", HttpStatusCode.NotFound)
                .ConfigureAwait(false);
            return true;
        }

        try
        {
            if (path.Equals("/api/unofficial_results/latest", StringComparison.OrdinalIgnoreCase))
            {
                var latest = _unofficialResults.GetLatestRace();
                if (latest is null)
                {
                    await WritePlainAsync(response, "No unofficial race results available", HttpStatusCode.NotFound)
                        .ConfigureAwait(false);
                    return true;
                }

                await WriteJsonAsync(response, latest, HttpStatusCode.OK).ConfigureAwait(false);
                return true;
            }

            if (path.Equals("/api/unofficial_results/info", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, _unofficialResults.GetAllRaceInfo(), HttpStatusCode.OK)
                    .ConfigureAwait(false);
                return true;
            }

            const string racePrefix = "/api/unofficial_results/race/";
            if (path.StartsWith(racePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var raceNumber = Uri.UnescapeDataString(path[racePrefix.Length..]);
                if (string.IsNullOrWhiteSpace(raceNumber))
                {
                    await WriteNotFoundAsync(response).ConfigureAwait(false);
                    return true;
                }

                var race = _unofficialResults.GetRaceByNumber(raceNumber);
                if (race is null)
                {
                    await WritePlainAsync(
                            response,
                            $"No unofficial results found for race {raceNumber}",
                            HttpStatusCode.NotFound)
                        .ConfigureAwait(false);
                    return true;
                }

                await WriteJsonAsync(response, race, HttpStatusCode.OK).ConfigureAwait(false);
                return true;
            }

            await WriteNotFoundAsync(response).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error serving unofficial results");
            await WriteInternalServerErrorAsync(response).ConfigureAwait(false);
            return true;
        }
    }

    private async Task<bool> TryServeViewsAsync(string path, HttpListenerResponse response)
    {
        if (_viewsRoot is null)
            return false;

        if (!path.Equals("/views", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/views/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Equals("/views", StringComparison.OrdinalIgnoreCase))
        {
            await WriteTextAsync(response, BuildViewsIndexHtml(), "text/html; charset=utf-8", HttpStatusCode.OK)
                .ConfigureAwait(false);
            return true;
        }

        var relativeUrl = path["/views/".Length..];
        relativeUrl = Uri.UnescapeDataString(relativeUrl);

        if (relativeUrl.Contains("..", StringComparison.Ordinal))
        {
            await WriteNotFoundAsync(response).ConfigureAwait(false);
            return true;
        }

        var slashIndex = relativeUrl.IndexOf('/');
        string relativeFs;
        if (slashIndex < 0)
        {
            relativeFs = Path.Combine(relativeUrl, "template.html");
        }
        else
        {
            relativeFs = relativeUrl.Replace('/', Path.DirectorySeparatorChar);
        }

        if (!TryResolveSafeFile(_viewsRoot, relativeFs, out var filePath) || !File.Exists(filePath))
        {
            await WriteNotFoundAsync(response).ConfigureAwait(false);
            return true;
        }

        await WriteFileAsync(response, filePath).ConfigureAwait(false);
        return true;
    }

    private string BuildViewsIndexHtml()
    {
        var views = new List<(string Name, string DisplayName, string Description)>();
        if (_viewsRoot is not null && Directory.Exists(_viewsRoot))
        {
            var discovery = new ViewDiscoveryService(_viewsRoot);
            discovery.DiscoverViews();
            foreach (var view in discovery.DiscoveredViews
                         .Where(v => v.IsValid)
                         .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                views.Add((view.Name, view.DisplayName, view.Description));
            }
        }

        var items = new StringBuilder();
        if (views.Count == 0)
        {
            items.AppendLine("""<li class="no-views">No valid views found. Create directories in the Views folder with template.html files.</li>""");
        }
        else
        {
            foreach (var (name, displayName, description) in views)
            {
                var descriptionHtml = string.IsNullOrEmpty(description)
                    ? ""
                    : $"""<div class="description">{System.Net.WebUtility.HtmlEncode(description)}</div>""";

                items.AppendLine($"""
                    <li>
                      <a href="/views/{Uri.EscapeDataString(name)}">{System.Net.WebUtility.HtmlEncode(displayName)}</a>
                      {descriptionHtml}
                    </li>
                    """);
            }
        }

        var versionText = ReadVersionBannerHtml();

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>WebLynx2 Views</title>
              <style>
                body {
                  font-family: Arial, sans-serif;
                  max-width: 800px;
                  margin: 0 auto;
                  padding: 20px;
                  background-color: #f5f5f5;
                }
                .container {
                  background-color: white;
                  padding: 30px;
                  border-radius: 8px;
                  box-shadow: 0 2px 10px rgba(0,0,0,0.1);
                }
                h1 {
                  color: #333;
                  text-align: center;
                  margin-bottom: 30px;
                }
                .views-list {
                  list-style: none;
                  padding: 0;
                }
                .views-list li {
                  margin: 15px 0;
                }
                .views-list a {
                  display: block;
                  padding: 15px 20px;
                  background-color: #007bff;
                  color: white;
                  text-decoration: none;
                  border-radius: 5px;
                  transition: background-color 0.3s;
                }
                .views-list a:hover {
                  background-color: #0056b3;
                }
                .description {
                  color: #666;
                  font-size: 14px;
                  margin-top: 5px;
                  padding: 0 4px;
                }
                .no-views {
                  text-align: center;
                  color: #666;
                  font-style: italic;
                  padding: 40px;
                }
                .version {
                  text-align: center;
                  color: #666;
                  font-size: 14px;
                  margin-bottom: 20px;
                  font-weight: 500;
                }
              </style>
            </head>
            <body>
              <div class="container">
                <h1>WebLynx2 Views</h1>
                {{versionText}}
                <ul class="views-list">
            {{items}}
                </ul>
              </div>
            </body>
            </html>
            """;
    }

    private string ReadVersionBannerHtml()
    {
        foreach (var candidate in new[]
                 {
                     _viewsRoot is null ? null : Path.Combine(_viewsRoot, "VERSION.txt"),
                     Path.Combine(AppContext.BaseDirectory, "VERSION.txt")
                 })
        {
            if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate))
                continue;

            try
            {
                var version = File.ReadAllText(candidate).Trim();
                if (!string.IsNullOrEmpty(version))
                    return $"""<div class="version">Version {System.Net.WebUtility.HtmlEncode(version)}</div>""";
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return "";
    }

    private static bool TryResolveSafeFile(string root, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        var combined = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSep = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = combined;
        return true;
    }

    private static async Task WriteFileAsync(HttpListenerResponse response, string filePath)
    {
        var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = GetContentType(filePath);
        response.ContentLength64 = bytes.Length;
        // Views/helpers change often during meet setup; avoid sticky browser/OBS caches.
        response.Headers["Cache-Control"] = "no-cache";

        try
        {
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }

    private static string GetContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".avif" => "image/avif",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };

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

    private static async Task WriteTextAsync(
        HttpListenerResponse response,
        string text,
        string contentType,
        HttpStatusCode statusCode)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = (int)statusCode;
        response.ContentType = contentType;
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

    private static Task WritePlainAsync(HttpListenerResponse response, string message, HttpStatusCode statusCode) =>
        WriteTextAsync(response, message, "text/plain; charset=utf-8", statusCode);

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
