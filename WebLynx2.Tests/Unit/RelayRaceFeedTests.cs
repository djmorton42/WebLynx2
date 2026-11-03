using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2.Parsing;
using WebLynx2.People;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class RelayRaceFeedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WebLynx2RelayFeed_" + Guid.NewGuid().ToString("N"));

    public RelayRaceFeedTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void StartList_StripsRosterPrefix_ResolvesKnownSkaters_AndLeavesIndividualsAlone()
    {
        var people = WritePeople(
            """
            104, Wong, Eugene, Toronto
            245, Wu, Alwyn, Newmarket
            2343, Lee, Pat, Club
            507, Aru, Slade, KW
            746, Wu, Alwyn, Newmarket
            """);
        var race = CreateManager(people);

        Send(race, StartListMessage(
            StartListLine(1, 10, "|104|245|1222|2343| TEAM LIGHT BLUE", "SSO", "16"),
            StartListLine(2, 11, "|507|746| TEAM RED", "SSO", "16"),
            StartListLine(3, 1251, "Eugene Wong", "Toronto", "16")));

        var lightBlue = race.CurrentRace.Racers.Single(r => r.Lane == 1);
        Assert.Equal("TEAM LIGHT BLUE", lightBlue.Name);
        Assert.Equal("SSO", lightBlue.Affiliation);
        Assert.Equal(["104", "245", "1222", "2343"], lightBlue.RelayMemberIds);
        Assert.Equal("Eugene", lightBlue.Roster[0].FirstName);
        Assert.Equal("Wong", lightBlue.Roster[0].LastName);
        Assert.Equal("Alwyn", lightBlue.Roster[1].FirstName);
        Assert.Equal("", lightBlue.Roster[2].FirstName);
        Assert.Equal("", lightBlue.Roster[2].LastName);
        Assert.Equal("1222", lightBlue.Roster[2].Id);
        Assert.Equal("Pat", lightBlue.Roster[3].FirstName);

        var red = race.CurrentRace.Racers.Single(r => r.Lane == 2);
        Assert.Equal("TEAM RED", red.Name);
        Assert.Equal(["507", "746"], red.RelayMemberIds);
        Assert.Equal("Slade", red.Roster[0].FirstName);

        var individual = race.CurrentRace.Racers.Single(r => r.Lane == 3);
        Assert.Equal("Eugene Wong", individual.Name);
        Assert.Equal("Toronto", individual.Affiliation);
        Assert.Empty(individual.RelayMemberIds);
        Assert.Empty(individual.Roster);
    }

    [Fact]
    public void StartedUpdate_KeepsRosterWhileApplyingLapChanges()
    {
        var people = WritePeople("104, Wong, Eugene, Toronto\n245, Wu, Alwyn, Newmarket\n");
        var race = CreateManager(people);
        Send(race, StartListMessage(StartListLine(1, 10, "|104|245| TEAM RED", "SSO", "16")));

        Send(race, string.Join(Environment.NewLine,
        [
            "*** StartedHeader ***",
            "Plc Ln  ReacTime Cum ST   Last ST  Best ST  Laps   Speed  Pace",
            StartedLine(1, "12"),
            "*** StartList/Started/ResultsTrailer ***"
        ]));

        var team = race.CurrentRace.Racers.Single();
        Assert.Equal("TEAM RED", team.Name);
        Assert.Equal(["104", "245"], team.RelayMemberIds);
        Assert.Equal("Eugene", team.Roster[0].FirstName);
        Assert.Equal(12m, team.LapsRemaining);
    }

    [Fact]
    public void RunningTime_DoesNotReloadPeopleFile()
    {
        var path = PeoplePath();
        WritePeopleFile(path, "104, Wong, Eugene, Toronto\n");
        var people = OpenPeople(path);
        var race = CreateManager(people);
        Send(race, StartListMessage(StartListLine(1, 10, "|104| TEAM RED", "SSO", "16")));
        Assert.Equal("Eugene", race.CurrentRace.Racers.Single().Roster[0].FirstName);

        WritePeopleFile(path, "104, Wong, Gene, Toronto\n");
        Send(race, "Running time: 1:02.5");

        Assert.Equal("Eugene", race.CurrentRace.Racers.Single().Roster[0].FirstName);
        Assert.Equal("TEAM RED", race.CurrentRace.Racers.Single().Name);
    }

    [Fact]
    public void RefreshRelayRosters_PicksUpPeopleFileEditWithoutNewFinishLynxMessage()
    {
        var path = PeoplePath();
        WritePeopleFile(path, "104, Wong, Eugene, Toronto\n");
        var people = OpenPeople(path);
        var race = CreateManager(people);
        Send(race, StartListMessage(StartListLine(1, 10, "|104| TEAM RED", "SSO", "16")));

        WritePeopleFile(path, "104, Wong, Gene, Toronto\n");
        race.RefreshRelayRosters();

        var team = race.CurrentRace.Racers.Single();
        Assert.Equal("Gene", team.Roster[0].FirstName);
        Assert.Equal("Wong", team.Roster[0].LastName);
        Assert.Equal("TEAM RED", team.Name);
        Assert.Equal(["104"], team.RelayMemberIds);
    }

    [Fact]
    public void ResultsMessage_ReloadsPeopleFile_AndDoesNotPutIdsBackIntoTheName()
    {
        var path = PeoplePath();
        WritePeopleFile(path, "104, Wong, Eugene, Toronto\n");
        var people = OpenPeople(path);
        var race = CreateManager(people);
        Send(race, StartListMessage(StartListLine(1, 10, "|104|245| TEAM RED", "SSO", "16")));

        WritePeopleFile(path, "104, Wong, Gene, Toronto\n245, Wu, Alwyn, Newmarket\n");
        Send(race, ResultsMessage(ResultsLine("1", 1, 10, "|104|245| TEAM RED", "SSO", "46.218")));

        var team = race.CurrentRace.Racers.Single();
        Assert.Equal("TEAM RED", team.Name);
        Assert.Equal("Gene", team.Roster[0].FirstName);
        Assert.Equal("Alwyn", team.Roster[1].FirstName);
        Assert.Equal(["104", "245"], team.RelayMemberIds);
        Assert.True(team.HasFinished);
    }

    [Fact]
    public void ResultsWithoutStartList_StripsPrefixAndResolvesRoster()
    {
        var people = WritePeople("104, Wong, Eugene, Toronto\n");
        var race = CreateManager(people);

        Send(race, ResultsMessage(ResultsLine("1", 2, 10, "|104| TEAM LIGHT BLUE", "SSO", "46.218")));

        var team = race.CurrentRace.Racers.Single();
        Assert.Equal("TEAM LIGHT BLUE", team.Name);
        Assert.Equal("SSO", team.Affiliation);
        Assert.Equal(["104"], team.RelayMemberIds);
        Assert.Equal("Eugene", team.Roster[0].FirstName);
        Assert.Equal("Wong", team.Roster[0].LastName);
    }

    private string PeoplePath() => Path.Combine(_dir, "Lynx.ppl");

    private LynxPeopleFile WritePeople(string text)
    {
        var path = PeoplePath();
        WritePeopleFile(path, text);
        return OpenPeople(path);
    }

    private static void WritePeopleFile(string path, string text)
    {
        var previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow;
        File.WriteAllText(path, text, Encoding.Latin1);
        File.SetLastWriteTimeUtc(path, previous.AddMinutes(1));
    }

    private static LynxPeopleFile OpenPeople(string path) =>
        new()
        {
            FilePath = path
        };

    private static RaceStateManager CreateManager(LynxPeopleFile people)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var manager = RaceFeedComposition.CreateRaceStateManager(loggerFactory);
        manager.People = people;
        return manager;
    }

    private static void Send(RaceStateManager manager, string text) =>
        manager.ProcessMessage(Encoding.Unicode.GetBytes(text), "relay-test");

    private static string StartListLine(int lane, int id, string name, string affiliation, string laps) =>
        $"{lane,-3} {id,-4} {name,-50} {affiliation,-30} {laps,-6}";

    private static string StartListMessage(params string[] racerLines)
    {
        var builder = new StringBuilder();
        builder.AppendLine("*** StartListHeader ***");
        builder.AppendLine("Event name         : Mixed Relay");
        builder.AppendLine("Ln  Id   Name                                               Affiliation                    Laps");
        builder.AppendLine("--- ---- -------------------------------------------------- ------------------------------ ------");
        foreach (var line in racerLines)
            builder.AppendLine(line);
        builder.AppendLine("*** StartList/Started/ResultsTrailer ***");
        return builder.ToString();
    }

    private static string StartedLine(int lane, string laps) =>
        $"{"",-3} {lane,-3} {"",-8} {"",-8} {"",-8} {"",-8} {laps,-6} {"0",-6} {"0",-6}";

    private static string ResultsLine(string place, int lane, int id, string name, string affiliation, string time) =>
        $"{place,-3} {lane,-3} {id,-4} {name,-50} {affiliation,-30} {time,-8}";

    private static string ResultsMessage(string racerLine)
    {
        var builder = new StringBuilder();
        builder.AppendLine("*** ResultsHeader[1] ***");
        builder.AppendLine("Event name         : Mixed Relay");
        builder.AppendLine("Plc Ln  Id   Name                                               Affiliation                    Time     Delta    ReacTime");
        builder.AppendLine("--- --- ---- -------------------------------------------------- ------------------------------ -------- -------- --------");
        builder.AppendLine(racerLine);
        builder.AppendLine("*** StartList/Started/ResultsTrailer ***");
        return builder.ToString();
    }
}
