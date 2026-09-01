using WebLynx2;
using WebLynx2.Api;
using WebLynx2.Models;
using WebLynx2.Tests.Helpers;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class AnnouncementOverrideServiceTests
{
    [Fact]
    public void Resolve_WhenInactive_ReturnsFinishLynxMessage()
    {
        var service = new AnnouncementOverrideService();

        Assert.False(service.IsActive);
        Assert.Equal("from lynx", service.Resolve("from lynx"));
        Assert.Null(service.Resolve(null));
    }

    [Fact]
    public void Apply_OverridesFinishLynxMessage()
    {
        var service = new AnnouncementOverrideService();

        service.Apply("forced text");

        Assert.True(service.IsActive);
        Assert.Equal("forced text", service.ForcedMessage);
        Assert.Equal("forced text", service.Resolve("from lynx"));
    }

    [Fact]
    public void Apply_EmptyString_SuppressesFinishLynxMessage()
    {
        var service = new AnnouncementOverrideService();

        service.Apply("");

        Assert.True(service.IsActive);
        Assert.Equal(string.Empty, service.Resolve("from lynx"));
    }

    [Fact]
    public void Clear_RestoresFinishLynxMessage()
    {
        var service = new AnnouncementOverrideService();
        service.Apply("forced");

        service.Clear();

        Assert.False(service.IsActive);
        Assert.Null(service.ForcedMessage);
        Assert.Equal("from lynx", service.Resolve("from lynx"));
    }
}

public class RaceDataApiMapperAnnouncementOverrideTests
{
    [Fact]
    public void Map_WithoutOverride_UsesRaceAnnouncement()
    {
        var store = new KeyValueStoreService();
        var overrideService = new AnnouncementOverrideService();
        var mapper = new RaceDataApiMapper(store, delayedDisplaySeconds: 5, overrideService);
        var race = RaceTestDataFactory.CreateSampleRace();

        var response = mapper.Map(race, "place");

        Assert.Equal(race.AnnouncementMessage, response.AnnouncementMessage);
    }

    [Fact]
    public void Map_WithActiveOverride_UsesForcedMessage()
    {
        var store = new KeyValueStoreService();
        var overrideService = new AnnouncementOverrideService();
        overrideService.Apply("Manual PA message");
        var mapper = new RaceDataApiMapper(store, delayedDisplaySeconds: 5, overrideService);
        var race = RaceTestDataFactory.CreateSampleRace();

        var response = mapper.Map(race, "place");

        Assert.Equal("Manual PA message", response.AnnouncementMessage);
        Assert.Equal("Race in progress", race.AnnouncementMessage);
    }

    [Fact]
    public void Map_AfterClear_UsesRaceAnnouncementAgain()
    {
        var store = new KeyValueStoreService();
        var overrideService = new AnnouncementOverrideService();
        overrideService.Apply("forced");
        overrideService.Clear();
        var mapper = new RaceDataApiMapper(store, delayedDisplaySeconds: 5, overrideService);
        var race = RaceTestDataFactory.CreateSampleRace();

        var response = mapper.Map(race, "place");

        Assert.Equal(race.AnnouncementMessage, response.AnnouncementMessage);
    }
}
