using System.Net;
using System.Text.Json;
using WebLynx2.Tests.Integration;
using Xunit;

namespace WebLynx2.Tests.Integration;

[Collection(nameof(RaceHttpServerCollection))]
public class CurrentRaceEndpointTests(RaceHttpServerFixture fixture)
{
    [Fact]
    public async Task GetCurrent_Returns200AndRawRaceData()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetCurrent_IncludesNestedPlaceData()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/current");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        var firstRacer = document.RootElement.GetProperty("racers")[0];
        Assert.True(firstRacer.TryGetProperty("place", out var place));
        Assert.True(place.TryGetProperty("placeText", out _));
        Assert.False(document.RootElement.TryGetProperty("keyValues", out _));
    }

    [Fact]
    public async Task GetCurrent_ResponseMatchesSeededState()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("api/race/current");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Race in progress", document.RootElement.GetProperty("announcementMessage").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("racers").GetArrayLength());
    }
}
