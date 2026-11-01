using System.Text;
using WebLynx2.People;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class LynxPeopleFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WebLynx2Ppl_" + Guid.NewGuid().ToString("N"));

    public LynxPeopleFileTests()
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
    public void Parse_TrimsSpacesAfterCommas()
    {
        var people = LynxPeopleFileParser.Parse(
            """
            104, Wong, Eugene, Toronto Speed Skating Club
            245, Wu, Alwyn, Newmarket
            """);

        var eugene = people["104"];
        Assert.Equal("Wong", eugene.LastName);
        Assert.Equal("Eugene", eugene.FirstName);
        Assert.Equal("Toronto Speed Skating Club", eugene.Affiliation);
        Assert.Equal("Alwyn", people["245"].FirstName);
    }

    [Fact]
    public void Parse_DuplicateId_UsesTheLaterRow()
    {
        var people = LynxPeopleFileParser.Parse(
            """
            104, Wong, Eugene, Toronto
            104, Wong, Gene, Toronto
            """);

        Assert.Equal("Gene", people["104"].FirstName);
    }

    [Fact]
    public void Parse_QuotedCommaInsideName_StaysOneField()
    {
        var people = LynxPeopleFileParser.Parse(
            """
            104,"Wong, Jr",Eugene,"Toronto, ON"
            """);

        Assert.Equal("Wong, Jr", people["104"].LastName);
        Assert.Equal("Eugene", people["104"].FirstName);
        Assert.Equal("Toronto, ON", people["104"].Affiliation);
    }

    [Fact]
    public void Resolve_PreservesSkatingOrderAndLeavesUnknownIdsBlank()
    {
        var people = LynxPeopleFileParser.Parse(
            """
            104, Wong, Eugene, Toronto
            245, Wu, Alwyn, Newmarket
            """);

        var roster = RelayRoster.Resolve(["245", "104", "1222"], people);

        Assert.Equal(["245", "104", "1222"], roster.Select(member => member.Id).ToArray());
        Assert.Equal("Alwyn", roster[0].FirstName);
        Assert.Equal("Wu", roster[0].LastName);
        Assert.Equal("Eugene", roster[1].FirstName);
        Assert.Equal("Wong", roster[1].LastName);
        Assert.Equal("", roster[2].FirstName);
        Assert.Equal("", roster[2].LastName);
    }

    [Fact]
    public void Refresh_Latin1File_ReadsAccentedNames()
    {
        var path = Path.Combine(_dir, "Lynx.ppl");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("104, José, Ana, Club\n"));

        var people = new LynxPeopleFile
        {
            FilePath = path,
            FileEncoding = Encoding.Latin1
        };

        Assert.True(people.Refresh());
        Assert.Equal("José", people.Find("104")!.LastName);
        Assert.Equal("Ana", people.Find("104")!.FirstName);
    }

    [Fact]
    public void Refresh_MissingFile_DoesNotThrow()
    {
        var people = new LynxPeopleFile
        {
            FilePath = Path.Combine(_dir, "missing.ppl")
        };

        Assert.False(people.Refresh());
        Assert.Null(people.Find("104"));
    }

    [Fact]
    public void Refresh_UnchangedFile_DoesNotReportAChange()
    {
        var path = Path.Combine(_dir, "Lynx.ppl");
        File.WriteAllText(path, "104, Wong, Eugene, Toronto\n", Encoding.Latin1);

        var people = new LynxPeopleFile
        {
            FilePath = path,
            FileEncoding = Encoding.Latin1
        };

        Assert.True(people.Refresh());
        Assert.False(people.Refresh());
        Assert.Equal("Eugene", people.Find("104")!.FirstName);
    }
}
