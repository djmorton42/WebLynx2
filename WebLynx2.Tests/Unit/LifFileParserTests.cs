using System.Globalization;
using System.Text;
using WebLynx2.UnofficialResults;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class LifFileParserTests
{
    private static readonly DateTime FixedUpdated = new(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    public static string CompleteRaceLif =>
        """
        8A,1,1,"Open Men A (500 111M) Heat, 1 + 3",,,,,,,10:59:07.1012
        1,1251,2,Wong,Eugene,Toronto,46.218,,45.872,,
        2,746,5,Wu,Alwyn,Newmarket,46.529,,46.529,,
        """;

    public static string IncompleteRaceLif =>
        """
        9A,1,1,"Open Women Heat",,,,,,,11:00:00.0000
        1,100,1,Alpha,Ann,ClubA,45.000,,
        2,101,2,Beta,Bob,ClubB,,,
        """;

    public static string RaceWithDnfLif =>
        """
        8B,1,1,"Open Men A Heat",,,,,,,11:02:10.3415
        1,746,5,Wu,Alwyn,Newmarket,45.872,,
        DNF,507,1,Aru,Slade,Kitchener-Waterloo,,,
        """;

    [Fact]
    public void ParseText_CompleteRace_MapsHeaderAndRacers()
    {
        var result = LifFileParser.ParseText(CompleteRaceLif, "/tmp/08A.lif", FixedUpdated);

        Assert.NotNull(result);
        Assert.Equal("8A", result!.RaceNumber);
        Assert.Equal(1, result.Heat);
        Assert.Equal(1, result.Round);
        Assert.Equal("Open Men A (500 111M) Heat, 1 + 3", result.EventName);
        Assert.Equal("10:59:07.1012", result.StartTime);
        Assert.Equal("/tmp/08A.lif", result.FilePath);
        Assert.Equal(FixedUpdated, result.LastUpdated);
        Assert.Equal(2, result.Racers.Count);

        var first = result.Racers[0];
        Assert.Equal("1", first.Position);
        Assert.Equal(1251, first.RacerId);
        Assert.Equal(2, first.LineNumber);
        Assert.Equal("Wong", first.LastName);
        Assert.Equal("Eugene", first.FirstName);
        Assert.Equal("Eugene Wong", first.Name);
        Assert.Equal("Toronto", first.Affiliation);
        Assert.Equal("46.218", first.FinishTimeRaw);
        Assert.Equal("46.218", first.FinishTimeFormatted);
        Assert.True(first.HasFinishTime);
        Assert.False(first.IsSpecialPosition);
        Assert.Equal(TimeSpan.FromSeconds(46.218), first.FinishTime);
    }

    [Fact]
    public void ParseText_EmptyContent_ReturnsNull()
    {
        Assert.Null(LifFileParser.ParseText("\n\n", "x.lif", FixedUpdated));
    }

    [Fact]
    public void ParseTimeToTimeSpan_RawSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(46.529), LifFileParser.ParseTimeToTimeSpan("46.529"));
    }

    [Fact]
    public void ParseTimeToTimeSpan_MinutesSeconds()
    {
        var time = LifFileParser.ParseTimeToTimeSpan("1:55.893");
        Assert.NotNull(time);
        Assert.Equal(1, time!.Value.Minutes);
        Assert.Equal(55, time.Value.Seconds);
    }

    [Fact]
    public void FormatTime_UnderOneMinute_UsesSeconds()
    {
        Assert.Equal("46.218", LifFileParser.FormatTime(TimeSpan.FromSeconds(46.218)));
    }

    [Fact]
    public void FormatTime_OverOneMinute_UsesMinutes()
    {
        Assert.Equal("1:55.893", LifFileParser.FormatTime(TimeSpan.FromSeconds(115.893)));
    }

    [Fact]
    public void IsSpecialPosition_RecognizesCommonCodes()
    {
        Assert.True(LifFileParser.IsSpecialPosition("DNF"));
        Assert.True(LifFileParser.IsSpecialPosition("dns"));
        Assert.True(LifFileParser.IsSpecialPosition("DSQ"));
        Assert.False(LifFileParser.IsSpecialPosition("1"));
        Assert.False(LifFileParser.IsSpecialPosition(""));
    }

    [Fact]
    public void IsResultsComplete_RequiresFinishOrSpecialForAllRacers()
    {
        var incomplete = LifFileParser.ParseText(IncompleteRaceLif, "a.lif", FixedUpdated)!;
        Assert.False(LifFileParser.IsResultsComplete(incomplete));

        var complete = LifFileParser.ParseText(CompleteRaceLif, "b.lif", FixedUpdated)!;
        Assert.True(LifFileParser.IsResultsComplete(complete));

        var withDnf = LifFileParser.ParseText(RaceWithDnfLif, "c.lif", FixedUpdated)!;
        Assert.True(LifFileParser.IsResultsComplete(withDnf));
        Assert.True(withDnf.Racers[1].IsSpecialPosition);
        Assert.False(withDnf.Racers[1].HasFinishTime);
    }

    [Fact]
    public void IsResultsComplete_EmptyRacers_IsIncomplete()
    {
        var race = new UnofficialRaceResult { RaceNumber = "1A" };
        Assert.False(LifFileParser.IsResultsComplete(race));
    }

    [Fact]
    public void ParseStartTime_AnchorsTimeOfDayToTodayOverride()
    {
        var today = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Local);
        var parsed = LifFileParser.ParseStartTime("14:25:33.4364", today);

        Assert.NotNull(parsed);
        Assert.Equal(today.Date, parsed!.Value.Date);
        Assert.Equal(14, parsed.Value.Hour);
        Assert.Equal(25, parsed.Value.Minute);
        Assert.Equal(33, parsed.Value.Second);
    }

    [Fact]
    public void ParseText_UsesRaceNumberFromContentNotFilename()
    {
        var result = LifFileParser.ParseText(CompleteRaceLif, "/tmp/08A-1-01.lif", FixedUpdated);
        Assert.Equal("8A", result!.RaceNumber);
    }
}
