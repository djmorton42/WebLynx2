using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2;
using WebLynx2.Api;
using WebLynx2.Tests.Unit;
using WebLynx2.UnofficialResults;
using Xunit;

namespace WebLynx2.Tests.Integration;

public class UnofficialResultsEndpointTests : IAsyncLifetime, IDisposable
{
    private readonly string _lifDir = Path.Combine(Path.GetTempPath(), "WebLynx2UnofficialHttp_" + Guid.NewGuid().ToString("N"));
    private RaceStateManager _raceState = null!;
    private UnofficialResultsCatalog _catalog = null!;
    private RaceHttpServer _server = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_lifDir);
        await File.WriteAllTextAsync(
            Path.Combine(_lifDir, "08A.lif"),
            LifFileParserTests.CompleteRaceLif,
            Encoding.Latin1);
        await File.WriteAllTextAsync(
            Path.Combine(_lifDir, "08B.lif"),
            LifFileParserTests.RaceWithDnfLif,
            Encoding.Latin1);

        _catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await _catalog.RefreshAsync(_lifDir);

        _port = GetFreePort();
        _raceState = RaceFeedComposition.CreateRaceStateManager(NullLoggerFactory.Instance);
        _server = new RaceHttpServer(
            NullLogger<RaceHttpServer>.Instance,
            _raceState,
            new KeyValueStoreService(),
            delayedDisplaySeconds: 5,
            unofficialResults: _catalog);

        await _server.StartAsync(_port);
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_lifDir))
                Directory.Delete(_lifDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task GetLatest_ReturnsMostRecentRace()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("api/unofficial_results/latest");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("8B", document.RootElement.GetProperty("raceNumber").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("racers").GetArrayLength());
    }

    [Fact]
    public async Task GetInfo_ReturnsSummariesNewestFirst()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("api/unofficial_results/info");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("8B", document.RootElement[0].GetProperty("raceNumber").GetString());
        Assert.Equal("8A", document.RootElement[1].GetProperty("raceNumber").GetString());
        Assert.Equal(2, document.RootElement[0].GetProperty("racerCount").GetInt32());
    }

    [Fact]
    public async Task GetRaceByNumber_ReturnsFullResult()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("api/unofficial_results/race/8A");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("8A", document.RootElement.GetProperty("raceNumber").GetString());
        Assert.Equal("Eugene Wong", document.RootElement.GetProperty("racers")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetRaceByNumber_Unknown_Returns404WithMessage()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("api/unofficial_results/race/ZZZ");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("ZZZ", body);
    }

    [Fact]
    public async Task GetLatest_WhenEmpty_Returns404()
    {
        var emptyCatalog = new UnofficialResultsCatalog();
        var port = GetFreePort();
        await using var server = new RaceHttpServer(
            NullLogger<RaceHttpServer>.Instance,
            RaceFeedComposition.CreateRaceStateManager(NullLoggerFactory.Instance),
            new KeyValueStoreService(),
            delayedDisplaySeconds: 5,
            unofficialResults: emptyCatalog);
        await server.StartAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        var response = await client.GetAsync("api/unofficial_results/latest");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("No unofficial race results available", body);
    }

    [Fact]
    public async Task GetInfo_WhenEmpty_ReturnsEmptyArray()
    {
        var emptyCatalog = new UnofficialResultsCatalog();
        var port = GetFreePort();
        await using var server = new RaceHttpServer(
            NullLogger<RaceHttpServer>.Instance,
            RaceFeedComposition.CreateRaceStateManager(NullLoggerFactory.Instance),
            new KeyValueStoreService(),
            delayedDisplaySeconds: 5,
            unofficialResults: emptyCatalog);
        await server.StartAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        var response = await client.GetAsync("api/unofficial_results/info");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", json.Trim());
    }

    private HttpClient CreateClient() =>
        new() { BaseAddress = new Uri($"http://127.0.0.1:{_port}/") };

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
