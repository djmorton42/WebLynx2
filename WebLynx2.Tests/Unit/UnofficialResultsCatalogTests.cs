using System.Text;
using WebLynx2.People;
using WebLynx2.UnofficialResults;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class UnofficialResultsCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WebLynx2Lif_" + Guid.NewGuid().ToString("N"));

    public UnofficialResultsCatalogTests()
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
    public async Task Refresh_MissingDirectory_IsNoOp()
    {
        var catalog = new UnofficialResultsCatalog();
        await catalog.RefreshAsync(Path.Combine(_dir, "does-not-exist"));
        Assert.Equal(0, catalog.Count);
        Assert.Null(catalog.GetLatestRace());
    }

    [Fact]
    public async Task Refresh_LoadsCompleteLifFiles()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "08A.lif"),
            LifFileParserTests.CompleteRaceLif,
            Encoding.Latin1);

        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await catalog.RefreshAsync(_dir);

        Assert.Equal(1, catalog.Count);
        Assert.Equal("8A", catalog.GetLatestRace()!.RaceNumber);
        Assert.NotNull(catalog.GetRaceByNumber("8A"));
        Assert.Single(catalog.GetAllRaceInfo());
    }

    [Fact]
    public async Task Refresh_IgnoresIncompleteRaces()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "incomplete.lif"),
            LifFileParserTests.IncompleteRaceLif,
            Encoding.Latin1);

        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await catalog.RefreshAsync(_dir);

        Assert.Equal(0, catalog.Count);
    }

    [Fact]
    public async Task Refresh_LoadsRaceWithDnfAsComplete()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "08B.lif"),
            LifFileParserTests.RaceWithDnfLif,
            Encoding.Latin1);

        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await catalog.RefreshAsync(_dir);

        Assert.Equal(1, catalog.Count);
        Assert.Equal("8B", catalog.GetRaceByNumber("8B")!.RaceNumber);
    }

    [Fact]
    public async Task Refresh_UnchangedFile_IsNotReparsed()
    {
        var path = Path.Combine(_dir, "08A.lif");
        await File.WriteAllTextAsync(path, LifFileParserTests.CompleteRaceLif, Encoding.Latin1);

        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await catalog.RefreshAsync(_dir);
        var first = catalog.GetRaceByNumber("8A")!;

        await catalog.RefreshAsync(_dir);
        var second = catalog.GetRaceByNumber("8A")!;

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Refresh_UpdatedFile_ReplacesResult()
    {
        var path = Path.Combine(_dir, "08A.lif");
        await File.WriteAllTextAsync(path, LifFileParserTests.CompleteRaceLif, Encoding.Latin1);

        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await catalog.RefreshAsync(_dir);
        Assert.Equal(2, catalog.GetRaceByNumber("8A")!.Racers.Count);

        await File.WriteAllTextAsync(
            path,
            """
            8A,1,1,"Open Men A (500 111M) Heat, 1 + 3",,,,,,,10:59:07.1012
            1,1251,2,Wong,Eugene,Toronto,46.218,,
            2,746,5,Wu,Alwyn,Newmarket,46.529,,
            3,999,3,Lee,Pat,ClubC,47.000,,
            """,
            Encoding.Latin1);
        await catalog.RefreshAsync(_dir);

        Assert.Equal(3, catalog.GetRaceByNumber("8A")!.Racers.Count);
    }

    [Fact]
    public async Task Refresh_DeletedFile_RemovesResult()
    {
        var path = Path.Combine(_dir, "08A.lif");
        await File.WriteAllTextAsync(path, LifFileParserTests.CompleteRaceLif, Encoding.Latin1);

        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await catalog.RefreshAsync(_dir);
        Assert.Equal(1, catalog.Count);

        File.Delete(path);
        await catalog.RefreshAsync(_dir);

        Assert.Equal(0, catalog.Count);
        Assert.Null(catalog.GetRaceByNumber("8A"));
    }

    [Fact]
    public async Task GetLatestRace_OrdersByRaceStartTime()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "early.lif"),
            """
            1A,1,1,"Early",,,,,,,10:00:00.0000
            1,1,1,A,A,X,40.000,,
            """,
            Encoding.Latin1);
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "late.lif"),
            """
            2A,1,1,"Late",,,,,,,12:00:00.0000
            1,2,1,B,B,Y,41.000,,
            """,
            Encoding.Latin1);

        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await catalog.RefreshAsync(_dir);

        Assert.Equal("2A", catalog.GetLatestRace()!.RaceNumber);
        Assert.Equal(["2A", "1A"], catalog.GetAllRaceInfo().Select(i => i.RaceNumber).ToArray());
    }

    [Fact]
    public async Task GetRaceByNumber_Unknown_ReturnsNull()
    {
        var catalog = new UnofficialResultsCatalog();
        await catalog.RefreshAsync(_dir);
        Assert.Null(catalog.GetRaceByNumber("ZZZ"));
    }

    [Fact]
    public async Task Refresh_RelayLif_ResolvesRosterFromPeopleFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "08A.lif"), RelayLif, Encoding.Latin1);
        var people = PeopleFile("104, Wong, Eugene, Toronto\n245, Wu, Alwyn, Newmarket\n");
        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1, People = people };

        await catalog.RefreshAsync(_dir);

        var team = catalog.GetRaceByNumber("8A")!.Racers[0];
        Assert.Equal("TEAM LIGHT BLUE", team.Name);
        Assert.Equal("Eugene", team.Roster[0].FirstName);
        Assert.Equal("Wong", team.Roster[0].LastName);
        Assert.Equal("Alwyn", team.Roster[1].FirstName);
        Assert.Equal("1222", team.Roster[2].Id);
        Assert.Equal("", team.Roster[2].FirstName);
        Assert.Equal("", team.Roster[2].LastName);

        var individual = catalog.GetRaceByNumber("8A")!.Racers[1];
        Assert.Equal("Eugene Wong", individual.Name);
        Assert.Empty(individual.Roster);
    }

    [Fact]
    public async Task Refresh_PeopleFileEdit_UpdatesRosterWhenLifFileIsUnchanged()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "08A.lif"), RelayLif, Encoding.Latin1);
        var peoplePath = Path.Combine(_dir, "Lynx.ppl");
        var people = PeopleFile("104, Wong, Eugene, Toronto\n", peoplePath);
        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1, People = people };
        await catalog.RefreshAsync(_dir);
        Assert.Equal("Eugene", catalog.GetRaceByNumber("8A")!.Racers[0].Roster[0].FirstName);

        var previousWrite = File.GetLastWriteTimeUtc(peoplePath);
        File.WriteAllText(peoplePath, "104, Wong, Gene, Toronto\n", Encoding.Latin1);
        File.SetLastWriteTimeUtc(peoplePath, previousWrite.AddMinutes(1));
        await catalog.RefreshAsync(_dir);

        var team = catalog.GetRaceByNumber("8A")!.Racers[0];
        Assert.Equal("Gene", team.Roster[0].FirstName);
        Assert.Equal("TEAM LIGHT BLUE", team.Name);
        Assert.Equal("Eugene Wong", catalog.GetRaceByNumber("8A")!.Racers[1].Name);
    }

    private LynxPeopleFile PeopleFile(string text, string? path = null)
    {
        path ??= Path.Combine(_dir, "Lynx.ppl");
        File.WriteAllText(path, text, Encoding.Latin1);
        return new LynxPeopleFile
        {
            FilePath = path,
            FileEncoding = Encoding.Latin1
        };
    }

    private const string RelayLif = """
        8A,1,1,"Open Mixed Relay",,,,,,,10:59:07.1012
        1,10,1,TEAM LIGHT BLUE,|104|245|1222|,SSO,46.218,,
        2,1251,2,Wong,Eugene,Toronto,46.529,,
        """;

    [Fact]
    public async Task Refresh_IgnoresSubdirectories()
    {
        var sub = Path.Combine(_dir, "nested");
        Directory.CreateDirectory(sub);
        await File.WriteAllTextAsync(
            Path.Combine(sub, "nested.lif"),
            LifFileParserTests.CompleteRaceLif,
            Encoding.Latin1);

        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await catalog.RefreshAsync(_dir);

        Assert.Equal(0, catalog.Count);
    }
}
