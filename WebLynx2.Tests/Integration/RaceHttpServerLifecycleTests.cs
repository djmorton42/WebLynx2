using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2;
using WebLynx2.Api;
using WebLynx2.Models;
using WebLynx2.Tests.Helpers;
using Xunit;

namespace WebLynx2.Tests.Integration;

public class RaceHttpServerLifecycleTests
{
    [Fact]
    public async Task Start_BindsToConfiguredPort()
    {
        var port = GetFreePort();
        await using var context = await CreateStartedServerAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        var response = await client.GetAsync("api/race/race-data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Stop_ReleasesPort()
    {
        var port = GetFreePort();
        var context = await CreateStartedServerAsync(port);
        await context.Server.StopAsync();

        Assert.Throws<SocketException>(() =>
        {
            using var probe = new TcpClient();
            probe.Connect("127.0.0.1", port);
        });
    }

    [Fact]
    public async Task StartTwice_ThrowsOrNoopsSafely()
    {
        var port = GetFreePort();
        await using var context = await CreateStartedServerAsync(port);

        var exception = await Record.ExceptionAsync(() => context.Server.StartAsync(port));

        Assert.NotNull(exception);
    }

    private static async Task<ServerContext> CreateStartedServerAsync(int port)
    {
        var raceState = RaceFeedComposition.CreateRaceStateManager(NullLoggerFactory.Instance);
        var keyValueStore = new KeyValueStoreService();
        var server = new RaceHttpServer(
            NullLogger<RaceHttpServer>.Instance,
            raceState,
            keyValueStore,
            delayedDisplaySeconds: 5);

        await server.StartAsync(port);
        return new ServerContext(server, raceState);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class ServerContext(RaceHttpServer server, RaceStateManager raceState) : IAsyncDisposable
    {
        public RaceHttpServer Server { get; } = server;
        public RaceStateManager RaceState { get; } = raceState;

        public async ValueTask DisposeAsync()
        {
            await Server.StopAsync();
        }
    }
}
