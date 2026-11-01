using System.Text.Json;
using WebLynx2.Api;
using WebLynx2.Models;
using WebLynx2.Tests.Helpers;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class RaceDataJsonSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = RaceHttpJsonSerializer.Options;

    [Fact]
    public void RaceDataResponse_UsesCamelCasePropertyNames()
    {
        var response = CreateSampleResponse();
        var json = JsonSerializer.Serialize(response, SerializerOptions);

        Assert.Contains("\"currentTime\"", json);
        Assert.Contains("\"halfLapModeEnabled\"", json);
        Assert.Contains("\"placeText\"", json);
        Assert.Contains("\"keyValues\"", json);
        Assert.Contains("\"viewConfig\"", json);
    }

    [Fact]
    public void TimeSpan_SerializesAsString()
    {
        var response = CreateSampleResponse();
        response.CurrentTime = TimeSpan.FromSeconds(83.456789);

        var json = JsonSerializer.Serialize(response, SerializerOptions);

        Assert.Contains("\"00:01:23.4567890\"", json);
    }

    [Fact]
    public void RaceStatus_SerializesAsInteger()
    {
        var response = CreateSampleResponse();
        response.Status = RaceStatus.Running;

        var json = JsonSerializer.Serialize(response, SerializerOptions);

        Assert.Contains("\"status\":1", json.Replace(" ", ""));
    }

    [Fact]
    public void LapsRemaining_SerializesAsNumber()
    {
        var response = CreateSampleResponse();
        response.Racers[0].LapsRemaining = 8.5m;

        var json = JsonSerializer.Serialize(response, SerializerOptions);

        Assert.Contains("\"lapsRemaining\":8.5", json.Replace(" ", ""));
    }

    [Fact]
    public void RelayRoster_SerializesCamelCaseMemberNames()
    {
        var race = RaceTestDataFactory.CreateSampleRace();
        race.Racers[0].Name = "TEAM RED";
        race.Racers[0].Roster =
        [
            new RelayRosterMember { Id = "104", FirstName = "Eugene", LastName = "Wong" }
        ];

        var json = JsonSerializer.Serialize(
            new RaceDataApiMapper(new KeyValueStoreService(), delayedDisplaySeconds: 5).Map(race, "place"),
            SerializerOptions);

        Assert.Contains("\"roster\"", json);
        Assert.Contains("\"id\":\"104\"", json.Replace(" ", ""));
        Assert.Contains("\"firstName\":\"Eugene\"", json.Replace(" ", ""));
        Assert.Contains("\"lastName\":\"Wong\"", json.Replace(" ", ""));
        Assert.DoesNotContain("|104|", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewConfig_SerializesNestedObjectsAndNumbers()
    {
        var store = new KeyValueStoreService();
        store.SetValue("laneColors.1", "#ffff00");
        store.SetValue("updateInterval", "250");
        var mapper = new RaceDataApiMapper(store, delayedDisplaySeconds: 5);

        var json = JsonSerializer.Serialize(mapper.Map(RaceTestDataFactory.CreateSampleRace(), "place"), SerializerOptions);

        Assert.Contains("\"viewConfig\"", json);
        Assert.Contains("\"laneColors\"", json);
        Assert.Contains("\"updateInterval\":250", json.Replace(" ", ""));
    }

    private static RaceDataApiResponse CreateSampleResponse()
    {
        var mapper = new RaceDataApiMapper(new KeyValueStoreService(), delayedDisplaySeconds: 5);
        return mapper.Map(RaceTestDataFactory.CreateSampleRace(), "place");
    }
}
