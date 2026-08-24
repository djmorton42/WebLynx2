using WebLynx2.Api;
using WebLynx2.Tests.Helpers;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class RacerSorterTests
{
    [Fact]
    public void SortByPlace_OrdersByPlaceThenLane()
    {
        var racers = RaceTestDataFactory.CreateRacersForSorting();

        var sorted = RacerSorter.Sort(racers, "place");

        Assert.Equal([2, 1, 3], sorted.Select(r => r.Lane).ToArray());
    }

    [Fact]
    public void SortByLane_OrdersByLaneOnly()
    {
        var racers = RaceTestDataFactory.CreateRacersForSorting();

        var sorted = RacerSorter.Sort(racers, "lane");

        Assert.Equal([1, 2, 3], sorted.Select(r => r.Lane).ToArray());
    }

    [Fact]
    public void SortByUnknownValue_DefaultsToPlace()
    {
        var racers = RaceTestDataFactory.CreateRacersForSorting();

        var sorted = RacerSorter.Sort(racers, "foo");

        Assert.Equal([2, 1, 3], sorted.Select(r => r.Lane).ToArray());
    }

    [Theory]
    [InlineData("place")]
    [InlineData("Place")]
    [InlineData("PLACE")]
    public void SortByPlace_IsCaseInsensitive(string sortBy)
    {
        var racers = RaceTestDataFactory.CreateRacersForSorting();

        var sorted = RacerSorter.Sort(racers, sortBy);

        Assert.Equal([2, 1, 3], sorted.Select(r => r.Lane).ToArray());
    }
}
