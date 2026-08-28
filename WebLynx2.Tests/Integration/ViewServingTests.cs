using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2;
using WebLynx2.Api;
using Xunit;

namespace WebLynx2.Tests.Integration;

public class ViewServingTests : IAsyncLifetime, IDisposable
{
    private readonly string _viewsRoot = Path.Combine(Path.GetTempPath(), "WebLynx2ViewTests_" + Guid.NewGuid().ToString("N"));
    private RaceStateManager _raceState = null!;
    private RaceHttpServer _server = null!;
    private int _port = 0;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_viewsRoot, "sample_view"));
        Directory.CreateDirectory(Path.Combine(_viewsRoot, "shared"));
        await File.WriteAllTextAsync(Path.Combine(_viewsRoot, "sample_view", "template.html"), "<html>sample view</html>");
        await File.WriteAllTextAsync(Path.Combine(_viewsRoot, "sample_view", "styles.css"), "body { color: red; }");
        await File.WriteAllTextAsync(Path.Combine(_viewsRoot, "sample_view", "description.txt"), "A sample overlay for tests.");
        await File.WriteAllTextAsync(Path.Combine(_viewsRoot, "shared", "weblynx-helpers.js"), "window.WebLynx = {};");

        _port = GetFreePort();
        _raceState = RaceFeedComposition.CreateRaceStateManager(NullLoggerFactory.Instance);
        _server = new RaceHttpServer(
            NullLogger<RaceHttpServer>.Instance,
            _raceState,
            new KeyValueStoreService(),
            delayedDisplaySeconds: 5,
            viewsRootPath: _viewsRoot);

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
            if (Directory.Exists(_viewsRoot))
                Directory.Delete(_viewsRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task GetView_ReturnsTemplateHtml()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("views/sample_view");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("sample view", body);
    }

    [Fact]
    public async Task GetViewAsset_ReturnsCss()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("views/sample_view/styles.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetSharedHelper_ReturnsJavascript()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("views/shared/weblynx-helpers.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task GetViewsIndex_ListsDiscoveredViews()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("views");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/views/sample_view", body);
        Assert.Contains("Sample View", body);
        Assert.Contains("A sample overlay for tests.", body);
        Assert.Contains("views-list", body);
        Assert.DoesNotContain("/views/shared", body);
    }

    [Fact]
    public async Task GetRoot_RedirectsToViews()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_port}/")
        };

        var response = await client.GetAsync("");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/views", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task GetUnknownView_Returns404()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("views/does_not_exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPathTraversal_Returns404()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("views/sample_view/../../etc/passwd");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
