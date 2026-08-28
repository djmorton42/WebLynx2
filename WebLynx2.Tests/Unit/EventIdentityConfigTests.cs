using WebLynx2;
using WebLynx2.Api;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class EventIdentityConfigTests
{
    [Fact]
    public void ApplyTo_WritesMeetTitleAndEventSubtitle()
    {
        var store = new KeyValueStoreService();

        EventIdentityConfig.ApplyTo(store, "Ontario Winter Games", "Orillia - Feb 20-21");

        Assert.Equal("Ontario Winter Games", store.GetValue(EventIdentityConfig.MeetTitleKey));
        Assert.Equal("Orillia - Feb 20-21", store.GetValue(EventIdentityConfig.EventSubtitleKey));
    }

    [Fact]
    public void ApplyTo_KeepsEmptyStrings()
    {
        var store = new KeyValueStoreService();

        EventIdentityConfig.ApplyTo(store, "", null);

        Assert.Equal("", store.GetValue(EventIdentityConfig.MeetTitleKey));
        Assert.Equal("", store.GetValue(EventIdentityConfig.EventSubtitleKey));
    }

    [Fact]
    public void ApplyTo_AppearsInRaceDataViewConfig()
    {
        var store = new KeyValueStoreService();
        EventIdentityConfig.ApplyTo(store, "Meet Title", "Location - Date");
        var mapper = new RaceDataApiMapper(store, delayedDisplaySeconds: 5);

        var response = mapper.Map(new Models.RaceData(), "place");

        Assert.Equal("Meet Title", response.KeyValues["meetTitle"]);
        Assert.Equal("Location - Date", response.KeyValues["eventSubtitle"]);
        Assert.Equal("Meet Title", response.ViewConfig["meetTitle"]);
        Assert.Equal("Location - Date", response.ViewConfig["eventSubtitle"]);
    }

    [Fact]
    public void ApplyTo_OverwritesPreviousValues()
    {
        var store = new KeyValueStoreService();
        EventIdentityConfig.ApplyTo(store, "Old", "Old Sub");
        EventIdentityConfig.ApplyTo(store, "New", "New Sub");

        Assert.Equal("New", store.GetValue(EventIdentityConfig.MeetTitleKey));
        Assert.Equal("New Sub", store.GetValue(EventIdentityConfig.EventSubtitleKey));
    }
}
