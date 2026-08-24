using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2;
using WebLynx2.Api;
using WebLynx2.Models;
using WebLynx2.Tests.Helpers;
using Xunit;

namespace WebLynx2.Tests.Integration;

public sealed class RaceHttpServerFixture : IAsyncLifetime
{
    public RaceStateManager RaceState { get; private set; } = null!;
    public KeyValueStoreService KeyValueStore { get; private set; } = null!;
    public RaceHttpServer Server { get; private set; } = null!;
    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}/";

    public async Task InitializeAsync()
    {
        Port = GetFreePort();
        KeyValueStore = new KeyValueStoreService();
        RaceState = CreateRaceStateManager();
        SeedRaceState();

        Server = new RaceHttpServer(
            NullLogger<RaceHttpServer>.Instance,
            RaceState,
            KeyValueStore,
            delayedDisplaySeconds: 5);

        await Server.StartAsync(Port);
    }

    public async Task DisposeAsync()
    {
        await Server.StopAsync();
    }

    public HttpClient CreateClient() =>
        new() { BaseAddress = new Uri(BaseUrl) };

    private void SeedRaceState()
    {
        var race = RaceTestDataFactory.CreateSampleRace();
        RaceState.ResetRace();
        RaceState.GetCurrentRaceState().Event = race.Event;
        RaceState.GetCurrentRaceState().Racers = race.Racers;
        RaceState.GetCurrentRaceState().CurrentTime = race.CurrentTime;
        RaceState.GetCurrentRaceState().Status = race.Status;
        RaceState.GetCurrentRaceState().LastUpdated = race.LastUpdated;
        RaceState.GetCurrentRaceState().AnnouncementMessage = race.AnnouncementMessage;

        KeyValueStore.SetValue("customKey1", "customValue1");
        KeyValueStore.SetValue("customKey2", "customValue2");
    }

    private static RaceStateManager CreateRaceStateManager()
    {
        return RaceFeedComposition.CreateRaceStateManager(NullLoggerFactory.Instance);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[CollectionDefinition(nameof(RaceHttpServerCollection))]
public class RaceHttpServerCollection : ICollectionFixture<RaceHttpServerFixture>;
