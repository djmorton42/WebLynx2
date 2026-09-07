using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2.Api;
using WebLynx2.Tests.Helpers;
using WebLynx2.UnofficialResults;
using Xunit;

namespace WebLynx2.Tests.Unit;

/// <summary>
/// Framework-agnostic regression coverage for HTTP routing and responses.
/// These tests call <see cref="RaceHttpApp"/> directly — no HttpListener/Kestrel.
/// </summary>
public class RaceHttpAppTests : IDisposable
{
    private readonly string _viewsRoot =
        Path.Combine(Path.GetTempPath(), "WebLynx2RaceHttpAppViews_" + Guid.NewGuid().ToString("N"));
    private readonly string _lifDir =
        Path.Combine(Path.GetTempPath(), "WebLynx2RaceHttpAppLif_" + Guid.NewGuid().ToString("N"));

    private readonly RaceStateManager _raceState;
    private readonly KeyValueStoreService _keyValueStore;
    private readonly UnofficialResultsCatalog _catalog;
    private readonly RaceHttpApp _app;

    public RaceHttpAppTests()
    {
        Directory.CreateDirectory(Path.Combine(_viewsRoot, "sample_view"));
        Directory.CreateDirectory(Path.Combine(_viewsRoot, "shared"));
        File.WriteAllText(Path.Combine(_viewsRoot, "sample_view", "template.html"), "<html>sample view</html>");
        File.WriteAllText(Path.Combine(_viewsRoot, "sample_view", "styles.css"), "body { color: red; }");
        File.WriteAllText(Path.Combine(_viewsRoot, "sample_view", "description.txt"), "A sample overlay for tests.");
        File.WriteAllText(Path.Combine(_viewsRoot, "shared", "weblynx-helpers.js"), "window.WebLynx = {};");

        Directory.CreateDirectory(_lifDir);
        File.WriteAllText(Path.Combine(_lifDir, "08A.lif"), LifFileParserTests.CompleteRaceLif, Encoding.Latin1);
        File.WriteAllText(Path.Combine(_lifDir, "08B.lif"), LifFileParserTests.RaceWithDnfLif, Encoding.Latin1);

        _catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        _catalog.RefreshAsync(_lifDir).GetAwaiter().GetResult();

        _keyValueStore = new KeyValueStoreService();
        _keyValueStore.SetValue("customKey1", "customValue1");
        _keyValueStore.SetValue("customKey2", "customValue2");
        _keyValueStore.SetValue("laneColors.1", "#ffff00");
        _keyValueStore.SetValue("updateInterval", "250");

        _raceState = RaceFeedComposition.CreateRaceStateManager(NullLoggerFactory.Instance);
        SeedRaceState(_raceState);

        _app = new RaceHttpApp(
            NullLogger.Instance,
            _raceState,
            _keyValueStore,
            delayedDisplaySeconds: 5,
            viewsRootPath: _viewsRoot,
            unofficialResults: _catalog);
    }

    public void Dispose()
    {
        TryDelete(_viewsRoot);
        TryDelete(_lifDir);
    }

    [Fact]
    public async Task Root_RedirectsToViews()
    {
        var response = await _app.HandleAsync(Get("/"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/views", response.Location);
    }

    [Fact]
    public async Task NonGet_Returns404()
    {
        var response = await _app.HandleAsync(new RaceHttpRequest
        {
            Method = "POST",
            Path = "/api/race/race-data"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _app.HandleAsync(Get("/api/race/unknown"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(response.Body);
    }

    [Fact]
    public async Task RaceData_ReturnsMappedJson()
    {
        var response = await _app.HandleAsync(Get("/api/race/race-data"));
        using var document = JsonDocument.Parse(response.BodyText);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.ContentType);
        Assert.Equal("Race in progress", document.RootElement.GetProperty("announcementMessage").GetString());
        Assert.Equal("Men's 1000m", document.RootElement.GetProperty("event").GetProperty("eventName").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("racers").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("halfLapModeEnabled").GetBoolean());
        Assert.Equal("customValue1", document.RootElement.GetProperty("keyValues").GetProperty("customKey1").GetString());
        Assert.Equal(250, document.RootElement.GetProperty("viewConfig").GetProperty("updateInterval").GetInt32());
    }

    [Theory]
    [InlineData("place", new[] { 1, 2, 3 })]
    [InlineData("lane", new[] { 1, 2, 3 })]
    public async Task RaceData_SortBy_QueryParam(string sortBy, int[] expectedLanes)
    {
        var response = await _app.HandleAsync(Get("/api/race/race-data", ("sortBy", sortBy)));
        using var document = JsonDocument.Parse(response.BodyText);

        var lanes = document.RootElement
            .GetProperty("racers")
            .EnumerateArray()
            .Select(r => r.GetProperty("lane").GetInt32())
            .ToArray();

        Assert.Equal(expectedLanes, lanes);
    }

    [Fact]
    public async Task CurrentRace_ReturnsRawRaceDataWithoutKeyValues()
    {
        var response = await _app.HandleAsync(Get("/api/race/current"));
        using var document = JsonDocument.Parse(response.BodyText);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Race in progress", document.RootElement.GetProperty("announcementMessage").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("status").GetInt32());
        Assert.True(document.RootElement.GetProperty("racers")[0].GetProperty("place").TryGetProperty("placeText", out _));
        Assert.False(document.RootElement.TryGetProperty("keyValues", out _));
    }

    [Fact]
    public async Task ViewsIndex_ListsDiscoveredViews()
    {
        var response = await _app.HandleAsync(Get("/views"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
        Assert.Contains("/views/sample_view", response.BodyText);
        Assert.Contains("Sample View", response.BodyText);
        Assert.Contains("A sample overlay for tests.", response.BodyText);
        Assert.DoesNotContain("/views/shared", response.BodyText);
    }

    [Fact]
    public async Task ViewTemplate_ReturnsHtml()
    {
        var response = await _app.HandleAsync(Get("/views/sample_view"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.ContentType);
        Assert.Equal("no-cache", response.CacheControl);
        Assert.Contains("sample view", response.BodyText);
    }

    [Fact]
    public async Task ViewAsset_ReturnsCss()
    {
        var response = await _app.HandleAsync(Get("/views/sample_view/styles.css"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css; charset=utf-8", response.ContentType);
        Assert.Equal("no-cache", response.CacheControl);
    }

    [Fact]
    public async Task SharedHelper_ReturnsJavascript()
    {
        var response = await _app.HandleAsync(Get("/views/shared/weblynx-helpers.js"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript; charset=utf-8", response.ContentType);
        Assert.Contains("WebLynx", response.BodyText);
    }

    [Fact]
    public async Task UnknownView_Returns404()
    {
        var response = await _app.HandleAsync(Get("/views/does_not_exist"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PathTraversal_Returns404()
    {
        var response = await _app.HandleAsync(Get("/views/sample_view/../../etc/passwd"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnofficialLatest_ReturnsMostRecentRace()
    {
        var response = await _app.HandleAsync(Get("/api/unofficial_results/latest"));
        using var document = JsonDocument.Parse(response.BodyText);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("8B", document.RootElement.GetProperty("raceNumber").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("racers").GetArrayLength());
    }

    [Fact]
    public async Task UnofficialInfo_ReturnsSummariesNewestFirst()
    {
        var response = await _app.HandleAsync(Get("/api/unofficial_results/info"));
        using var document = JsonDocument.Parse(response.BodyText);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("8B", document.RootElement[0].GetProperty("raceNumber").GetString());
        Assert.Equal("8A", document.RootElement[1].GetProperty("raceNumber").GetString());
    }

    [Fact]
    public async Task UnofficialRaceByNumber_ReturnsFullResult()
    {
        var response = await _app.HandleAsync(Get("/api/unofficial_results/race/8A"));
        using var document = JsonDocument.Parse(response.BodyText);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("8A", document.RootElement.GetProperty("raceNumber").GetString());
        Assert.Equal("Eugene Wong", document.RootElement.GetProperty("racers")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task UnofficialRaceByNumber_Unknown_Returns404WithMessage()
    {
        var response = await _app.HandleAsync(Get("/api/unofficial_results/race/ZZZ"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("ZZZ", response.BodyText);
    }

    [Fact]
    public async Task UnofficialLatest_WhenEmpty_Returns404()
    {
        var app = CreateApp(unofficialResults: new UnofficialResultsCatalog());

        var response = await app.HandleAsync(Get("/api/unofficial_results/latest"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("No unofficial race results available", response.BodyText);
    }

    [Fact]
    public async Task UnofficialInfo_WhenEmpty_ReturnsEmptyArray()
    {
        var app = CreateApp(unofficialResults: new UnofficialResultsCatalog());

        var response = await app.HandleAsync(Get("/api/unofficial_results/info"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", response.BodyText.Trim());
    }

    [Fact]
    public async Task Unofficial_WhenCatalogMissing_Returns404()
    {
        var app = CreateApp(unofficialResults: null);

        var response = await app.HandleAsync(Get("/api/unofficial_results/latest"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Unofficial results are not available", response.BodyText);
    }

    [Theory]
    [InlineData("file.html", "text/html; charset=utf-8")]
    [InlineData("file.css", "text/css; charset=utf-8")]
    [InlineData("file.js", "text/javascript; charset=utf-8")]
    [InlineData("file.png", "image/png")]
    [InlineData("file.unknown", "application/octet-stream")]
    public void GetContentType_MapsExtensions(string fileName, string expected)
    {
        Assert.Equal(expected, RaceHttpApp.GetContentType(fileName));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/views/", "/views")]
    [InlineData("/api/race/race-data", "/api/race/race-data")]
    public void NormalizePath_TrimsTrailingSlash(string? input, string expected)
    {
        Assert.Equal(expected, RaceHttpApp.NormalizePath(input));
    }

    private RaceHttpApp CreateApp(UnofficialResultsCatalog? unofficialResults) =>
        new(
            NullLogger.Instance,
            _raceState,
            _keyValueStore,
            delayedDisplaySeconds: 5,
            viewsRootPath: _viewsRoot,
            unofficialResults: unofficialResults);

    private static RaceHttpRequest Get(string path, params (string Key, string Value)[] query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in query)
            dict[key] = value;

        return new RaceHttpRequest
        {
            Method = "GET",
            Path = path,
            Query = dict
        };
    }

    private static void SeedRaceState(RaceStateManager raceState)
    {
        var race = RaceTestDataFactory.CreateSampleRace();
        raceState.ResetRace();
        raceState.GetCurrentRaceState().Event = race.Event;
        raceState.GetCurrentRaceState().Racers = race.Racers;
        raceState.GetCurrentRaceState().CurrentTime = race.CurrentTime;
        raceState.GetCurrentRaceState().Status = race.Status;
        raceState.GetCurrentRaceState().LastUpdated = race.LastUpdated;
        raceState.GetCurrentRaceState().AnnouncementMessage = race.AnnouncementMessage;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
