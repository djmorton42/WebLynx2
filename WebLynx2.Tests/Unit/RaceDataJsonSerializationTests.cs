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

    private static RaceDataApiResponse CreateSampleResponse()
    {
        var mapper = new RaceDataApiMapper(new KeyValueStoreService(), delayedDisplaySeconds: 5);
        return mapper.Map(RaceTestDataFactory.CreateSampleRace(), "place");
    }
}
