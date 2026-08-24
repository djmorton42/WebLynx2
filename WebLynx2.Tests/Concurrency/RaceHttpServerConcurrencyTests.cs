using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2;
using WebLynx2.Api;
using WebLynx2.Models;
using WebLynx2.Tests.Helpers;
using Xunit;

namespace WebLynx2.Tests.Concurrency;

public class RaceHttpServerConcurrencyTests : IAsyncLifetime
{
    private RaceStateManager _raceState = null!;
    private KeyValueStoreService _keyValueStore = null!;
    private RaceHttpServer _server = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        _port = GetFreePort();
        _keyValueStore = new KeyValueStoreService();
        _raceState = RaceFeedComposition.CreateRaceStateManager(NullLoggerFactory.Instance);
        SeedRaceState();

        _server = new RaceHttpServer(
            NullLogger<RaceHttpServer>.Instance,
            _raceState,
            _keyValueStore,
            delayedDisplaySeconds: 5);

        await _server.StartAsync(_port);
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
    }

    [Fact]
    public async Task ConcurrentReads_AllReturn200()
    {
        const int clientCount = 50;
        const int requestsPerClient = 20;
        var baseUrl = $"http://127.0.0.1:{_port}/";

        var tasks = Enumerable.Range(0, clientCount).Select(async _ =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            for (var i = 0; i < requestsPerClient; i++)
            {
                var response = await client.GetAsync("api/race/race-data");
                response.EnsureSuccessStatusCode();
            }
        });

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ConcurrentReads_ResponseIsValidJson()
    {
        const int clientCount = 30;
        const int requestsPerClient = 10;
        var baseUrl = $"http://127.0.0.1:{_port}/";

        var tasks = Enumerable.Range(0, clientCount).Select(async _ =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            for (var i = 0; i < requestsPerClient; i++)
            {
                var json = await client.GetStringAsync("api/race/race-data");
                using var document = JsonDocument.Parse(json);
                Assert.True(document.RootElement.GetProperty("halfLapModeEnabled").GetBoolean());
            }
        });

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ConcurrentReadsWhileStateUpdates_NoServerCrash()
    {
        var baseUrl = $"http://127.0.0.1:{_port}/";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var updateTask = Task.Run(async () =>
        {
            var counter = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                var race = _raceState.GetCurrentRaceState();
                race.CurrentTime = TimeSpan.FromSeconds(counter++ % 120);
                race.LastUpdated = DateTime.UtcNow;
                await Task.Delay(10, cts.Token);
            }
        }, cts.Token);

        var pollTasks = Enumerable.Range(0, 30).Select(async _ =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            while (!cts.Token.IsCancellationRequested)
            {
                var response = await client.GetAsync("api/race/race-data", cts.Token);
                response.EnsureSuccessStatusCode();
                await Task.Delay(100, cts.Token);
            }
        });

        var exception = await Record.ExceptionAsync(async () =>
        {
            await Task.WhenAll(pollTasks.Append(updateTask));
        });

        Assert.True(exception is null or OperationCanceledException);
    }

    [Fact]
    public async Task HighFrequencyPolling_MeetsLatencyBudget()
    {
        var baseUrl = $"http://127.0.0.1:{_port}/";
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var latencies = new List<long>();

        for (var i = 0; i < 10; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await client.GetAsync("api/race/race-data");
            stopwatch.Stop();
            response.EnsureSuccessStatusCode();
            latencies.Add(stopwatch.ElapsedMilliseconds);
            await Task.Delay(100);
        }

        latencies.Sort();
        var p99Index = Math.Min(latencies.Count - 1, (int)Math.Ceiling(latencies.Count * 0.99) - 1);
        var p99 = latencies[p99Index];

        Assert.True(p99 <= 100, $"p99 latency was {p99}ms, expected <= 100ms");
    }

    [Fact]
    public async Task ConcurrentMixedEndpoints_AllSucceed()
    {
        const int iterations = 50;
        var baseUrl = $"http://127.0.0.1:{_port}/";

        var tasks = Enumerable.Range(0, iterations).Select(async i =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            if (i % 2 == 0)
            {
                var response = await client.GetAsync("api/race/race-data");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            else
            {
                var response = await client.GetAsync("api/race/current");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        });

        var exception = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
        Assert.Null(exception);
    }

    private void SeedRaceState()
    {
        var race = RaceTestDataFactory.CreateSampleRace();
        _raceState.ResetRace();
        var current = _raceState.GetCurrentRaceState();
        current.Event = race.Event;
        current.Racers = race.Racers;
        current.CurrentTime = race.CurrentTime;
        current.Status = race.Status;
        current.LastUpdated = race.LastUpdated;
        current.AnnouncementMessage = race.AnnouncementMessage;
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
