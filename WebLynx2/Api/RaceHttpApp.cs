using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.UnofficialResults;

namespace WebLynx2.Api;

/// <summary>
/// Framework-agnostic HTTP application: routes and response bodies without HttpListener/Kestrel.
/// </summary>
public sealed class RaceHttpApp(
    ILogger logger,
    RaceStateManager raceState,
    KeyValueStoreService keyValueStore,
    int delayedDisplaySeconds,
    string? viewsRootPath = null,
    UnofficialResultsCatalog? unofficialResults = null,
    AnnouncementOverrideService? announcementOverride = null)
{
    private readonly RaceDataApiMapper _mapper =
        new(keyValueStore, delayedDisplaySeconds, announcementOverride);
    private readonly string? _viewsRoot = string.IsNullOrWhiteSpace(viewsRootPath)
        ? null
        : Path.GetFullPath(viewsRootPath);
    private readonly UnofficialResultsCatalog? _unofficialResults = unofficialResults;

    public Task<RaceHttpResponse> HandleAsync(RaceHttpRequest request)
    {
        try
        {
            return Task.FromResult(Handle(request));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling HTTP request");
            return Task.FromResult(RaceHttpResponse.InternalServerError());
        }
    }

    private RaceHttpResponse Handle(RaceHttpRequest request)
    {
        var path = NormalizePath(request.Path);

        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            return RaceHttpResponse.NotFound();

        if (path.Equals("/", StringComparison.Ordinal))
            return RaceHttpResponse.Redirect("/views");

        if (path.Equals("/api/race/race-data", StringComparison.OrdinalIgnoreCase))
        {
            var sortBy = request.GetQuery("sortBy") ?? "place";
            var apiResponse = _mapper.Map(raceState.GetCurrentRaceState(), sortBy);
            return RaceHttpResponse.Json(apiResponse);
        }

        if (path.Equals("/api/race/current", StringComparison.OrdinalIgnoreCase))
            return RaceHttpResponse.Json(raceState.GetCurrentRaceState());

        if (TryServeUnofficialResults(path, out var unofficialResponse))
            return unofficialResponse;

        if (TryServeViews(path, out var viewsResponse))
            return viewsResponse;

        return RaceHttpResponse.NotFound();
    }

    private bool TryServeUnofficialResults(string path, out RaceHttpResponse response)
    {
        response = RaceHttpResponse.NotFound();
        if (!path.StartsWith("/api/unofficial_results", StringComparison.OrdinalIgnoreCase))
            return false;

        if (_unofficialResults is null)
        {
            response = RaceHttpResponse.Text(
                "Unofficial results are not available",
                statusCode: HttpStatusCode.NotFound);
            return true;
        }

        try
        {
            if (path.Equals("/api/unofficial_results/latest", StringComparison.OrdinalIgnoreCase))
            {
                var latest = _unofficialResults.GetLatestRace();
                response = latest is null
                    ? RaceHttpResponse.Text(
                        "No unofficial race results available",
                        statusCode: HttpStatusCode.NotFound)
                    : RaceHttpResponse.Json(latest);
                return true;
            }

            if (path.Equals("/api/unofficial_results/info", StringComparison.OrdinalIgnoreCase))
            {
                response = RaceHttpResponse.Json(_unofficialResults.GetAllRaceInfo());
                return true;
            }

            const string racePrefix = "/api/unofficial_results/race/";
            if (path.StartsWith(racePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var raceNumber = Uri.UnescapeDataString(path[racePrefix.Length..]);
                if (string.IsNullOrWhiteSpace(raceNumber))
                {
                    response = RaceHttpResponse.NotFound();
                    return true;
                }

                var race = _unofficialResults.GetRaceByNumber(raceNumber);
                response = race is null
                    ? RaceHttpResponse.Text(
                        $"No unofficial results found for race {raceNumber}",
                        statusCode: HttpStatusCode.NotFound)
                    : RaceHttpResponse.Json(race);
                return true;
            }

            response = RaceHttpResponse.NotFound();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error serving unofficial results");
            response = RaceHttpResponse.InternalServerError();
            return true;
        }
    }

    private bool TryServeViews(string path, out RaceHttpResponse response)
    {
        response = RaceHttpResponse.NotFound();
        if (_viewsRoot is null)
            return false;

        if (!path.Equals("/views", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/views/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Equals("/views", StringComparison.OrdinalIgnoreCase))
        {
            response = RaceHttpResponse.Text(
                BuildViewsIndexHtml(),
                "text/html; charset=utf-8");
            return true;
        }

        var relativeUrl = path["/views/".Length..];
        relativeUrl = Uri.UnescapeDataString(relativeUrl);

        if (relativeUrl.Contains("..", StringComparison.Ordinal))
        {
            response = RaceHttpResponse.NotFound();
            return true;
        }

        var slashIndex = relativeUrl.IndexOf('/');
        string relativeFs;
        if (slashIndex < 0)
            relativeFs = Path.Combine(relativeUrl, "template.html");
        else
            relativeFs = relativeUrl.Replace('/', Path.DirectorySeparatorChar);

        if (!TryResolveSafeFile(_viewsRoot, relativeFs, out var filePath) || !File.Exists(filePath))
        {
            response = RaceHttpResponse.NotFound();
            return true;
        }

        var bytes = File.ReadAllBytes(filePath);
        response = RaceHttpResponse.Bytes(
            bytes,
            GetContentType(filePath),
            cacheControl: "no-cache");
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
                    : $"""<div class="description">{WebUtility.HtmlEncode(description)}</div>""";

                items.AppendLine($"""
                    <li>
                      <a href="/views/{Uri.EscapeDataString(name)}">{WebUtility.HtmlEncode(displayName)}</a>
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
                    return $"""<div class="version">Version {WebUtility.HtmlEncode(version)}</div>""";
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

    public static bool TryResolveSafeFile(string root, string relativePath, out string fullPath)
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

    public static string GetContentType(string filePath) =>
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

    public static string NormalizePath(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return "/";

        var path = absolutePath.TrimEnd('/');
        return string.IsNullOrEmpty(path) ? "/" : path;
    }
}
