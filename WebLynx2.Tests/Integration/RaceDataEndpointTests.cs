using System.Net;
using System.Text.Json;
using WebLynx2.Tests.Integration;
using Xunit;

namespace WebLynx2.Tests.Integration;

[Collection(nameof(RaceHttpServerCollection))]
public class RaceDataEndpointTests(RaceHttpServerFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetRaceData_Returns200AndJson()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/race-data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetRaceData_ResponseMatchesSeededState()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/race-data");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Race in progress", document.RootElement.GetProperty("announcementMessage").GetString());
        Assert.Equal("Men's 1000m", document.RootElement.GetProperty("event").GetProperty("eventName").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("racers").GetArrayLength());
    }

    [Fact]
    public async Task GetRaceData_SortByPlace_QueryParam()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/race-data?sortBy=place");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var lanes = document.RootElement
            .GetProperty("racers")
            .EnumerateArray()
            .Select(r => r.GetProperty("lane").GetInt32())
            .ToArray();

        Assert.Equal([1, 2, 3], lanes);
    }

    [Fact]
    public async Task GetRaceData_SortByLane_QueryParam()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/race-data?sortBy=lane");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var lanes = document.RootElement
            .GetProperty("racers")
            .EnumerateArray()
            .Select(r => r.GetProperty("lane").GetInt32())
            .ToArray();

        Assert.Equal([1, 2, 3], lanes);
    }

    [Fact]
    public async Task GetRaceData_IncludesHalfLapModeEnabled()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/race-data");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.GetProperty("halfLapModeEnabled").GetBoolean());
    }

    [Fact]
    public async Task GetRaceData_IncludesKeyValues()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/race-data");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var keyValues = document.RootElement.GetProperty("keyValues");
        Assert.Equal("customValue1", keyValues.GetProperty("customKey1").GetString());
        Assert.Equal("customValue2", keyValues.GetProperty("customKey2").GetString());
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/unknown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
