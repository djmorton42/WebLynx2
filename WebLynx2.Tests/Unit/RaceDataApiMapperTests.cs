using WebLynx2;
using WebLynx2.Api;
using WebLynx2.Models;
using WebLynx2.Tests.Helpers;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class RaceDataApiMapperTests
{
    private readonly KeyValueStoreService _keyValueStore = new();
    private readonly RaceDataApiMapper _mapper;

    public RaceDataApiMapperTests()
    {
        _mapper = new RaceDataApiMapper(_keyValueStore, delayedDisplaySeconds: 5);
    }

    [Fact]
    public void Map_EmptyRace_ReturnsValidResponse()
    {
        var race = new RaceData();

        var response = _mapper.Map(race, "place");

        Assert.Null(response.CurrentTime);
        Assert.Equal(RaceStatus.NotStarted, response.Status);
        Assert.Empty(response.Racers);
        Assert.True(response.HalfLapModeEnabled);
    }

    [Fact]
    public void Map_IncludesKeyValuesFromStore()
    {
        _keyValueStore.SetValue("customKey1", "customValue1");
        _keyValueStore.SetValue("customKey2", "customValue2");

        var response = _mapper.Map(new RaceData(), "place");

        Assert.Equal("customValue1", response.KeyValues["customKey1"]);
        Assert.Equal("customValue2", response.KeyValues["customKey2"]);
    }

    [Fact]
    public void Map_IncludesNestedViewConfigFromFlatKeys()
    {
        _keyValueStore.SetValue("laneColors.1", "#ffff00");
        _keyValueStore.SetValue("updateInterval", "250");

        var response = _mapper.Map(new RaceData(), "place");

        Assert.Equal(250, response.ViewConfig["updateInterval"]);
        var laneColors = Assert.IsType<Dictionary<string, object>>(response.ViewConfig["laneColors"]);
        Assert.Equal("#ffff00", laneColors["1"]);
        Assert.Equal("#ffff00", response.KeyValues["laneColors.1"]);
    }

    [Fact]
    public void Map_HalfLapModeEnabled_AlwaysTrue()
    {
        var response = _mapper.Map(new RaceData(), "place");

        Assert.True(response.HalfLapModeEnabled);
    }

    [Fact]
    public void Map_RacerFields_MappedCorrectly()
    {
        var race = RaceTestDataFactory.CreateSampleRace();
        var source = race.Racers.First(r => r.Lane == 1);

        var response = _mapper.Map(race, "place");
        var mapped = response.Racers.First(r => r.Lane == 1);

        Assert.Equal(source.Id, mapped.Id);
        Assert.Equal(source.Name, mapped.Name);
        Assert.Equal(source.Affiliation, mapped.Affiliation);
        Assert.Equal(source.Place.PlaceText, mapped.PlaceText);
        Assert.Equal(source.Place.HasPlaceData, mapped.HasPlaceData);
        Assert.Equal(source.ReactionTime, mapped.ReactionTime);
        Assert.Equal(source.CumulativeSplitTime, mapped.CumulativeSplitTime);
        Assert.Equal(source.LastSplitTime, mapped.LastSplitTime);
        Assert.Equal(source.BestSplitTime, mapped.BestSplitTime);
        Assert.Equal(source.LapsRemaining, mapped.LapsRemaining);
        Assert.Equal(source.Speed, mapped.Speed);
        Assert.Equal(source.Pace, mapped.Pace);
        Assert.Equal(source.FinalTime, mapped.FinalTime);
        Assert.Equal(source.DeltaTime, mapped.DeltaTime);
        Assert.Equal(source.HasFinished, mapped.HasFinished);
    }

    [Fact]
    public void Map_HasFirstCrossing_TrueWhenSplitTimesPresent()
    {
        var race = RaceTestDataFactory.CreateSampleRace();

        var response = _mapper.Map(race, "place");
        var withSplit = response.Racers.First(r => r.Lane == 1);

        Assert.True(withSplit.HasFirstCrossing);
    }

    [Fact]
    public void Map_HasFirstCrossing_FalseWhenNoSplits()
    {
        var race = RaceTestDataFactory.CreateSampleRace();

        var response = _mapper.Map(race, "place");
        var withoutSplit = response.Racers.First(r => r.Lane == 3);

        Assert.False(withoutSplit.HasFirstCrossing);
    }

    [Fact]
    public void Map_DelayedLapsRemaining_UsesConfiguredDelay()
    {
        var race = new RaceData
        {
            Racers =
            [
                new Racer
                {
                    Lane = 1,
                    LapsRemaining = 4m
                }
            ]
        };
        race.Racers[0].LapsRemaining = 3m;
        race.Racers[0].LapCountLastChanged = DateTime.UtcNow;

        var response = _mapper.Map(race, "place");

        Assert.Equal(4m, response.Racers[0].DelayedLapsRemaining);
    }

    [Fact]
    public void Map_SortByPlace_AppliedToRacersList()
    {
        var race = RaceTestDataFactory.CreateSampleRace();

        var response = _mapper.Map(race, "place");

        Assert.Equal([1, 2, 3], response.Racers.Select(r => r.Lane).ToArray());
    }

    [Fact]
    public void Map_SortByLane_AppliedToRacersList()
    {
        var race = RaceTestDataFactory.CreateSampleRace();

        var response = _mapper.Map(race, "lane");

        Assert.Equal([1, 2, 3], response.Racers.Select(r => r.Lane).ToArray());
    }

    [Fact]
    public void Map_EventAndRaceMetadata_Preserved()
    {
        var race = RaceTestDataFactory.CreateSampleRace();

        var response = _mapper.Map(race, "place");

        Assert.Equal(race.CurrentTime, response.CurrentTime);
        Assert.Equal(race.Event?.EventName, response.Event?.EventName);
        Assert.Equal(race.Status, response.Status);
        Assert.Equal(race.LastUpdated, response.LastUpdated);
        Assert.Equal(race.AnnouncementMessage, response.AnnouncementMessage);
    }
}
