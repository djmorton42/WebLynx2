using WebLynx2.Parsing;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class RelayRosterNameTests
{
    [Theory]
    [InlineData("|104|245|1222|2343| TEAM LIGHT BLUE", "TEAM LIGHT BLUE", "104|245|1222|2343")]
    [InlineData("|507|746| TEAM RED", "TEAM RED", "507|746")]
    [InlineData("|104| TEAM RED", "TEAM RED", "104")]
    [InlineData("  |104|245| TEAM RED  ", "TEAM RED", "104|245")]
    public void TrySplit_RosterPrefix_ReturnsTeamNameAndMemberIds(string combined, string team, string idsJoined)
    {
        var split = RelayRosterName.TrySplit(combined, out var teamName, out var memberIds);

        Assert.True(split);
        Assert.Equal(team, teamName);
        Assert.Equal(idsJoined.Split('|'), memberIds);
    }

    [Theory]
    [InlineData("Eugene Wong")]
    [InlineData("TEAM LIGHT BLUE")]
    [InlineData("Wong, Eugene")]
    [InlineData("Team | Red")]
    [InlineData("|104|245|")]
    [InlineData("|104|245|TEAM RED")]
    public void TrySplit_NameWithoutRosterPrefix_StaysIntact(string name)
    {
        var split = RelayRosterName.TrySplit(name, out var teamName, out var memberIds);

        Assert.False(split);
        Assert.Equal(name, teamName);
        Assert.Empty(memberIds);
    }

    [Theory]
    [InlineData("|104|245|1222|", "104|245|1222")]
    [InlineData("|8|", "8")]
    public void TryParseMemberIds_PipeList_ReturnsIdsInOrder(string firstName, string idsJoined)
    {
        var parsed = RelayRosterName.TryParseMemberIds(firstName, out var memberIds);

        Assert.True(parsed);
        Assert.Equal(idsJoined.Split('|'), memberIds);
    }

    [Theory]
    [InlineData("Eugene")]
    [InlineData("|104|245| extra")]
    [InlineData("|104|24a|")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseMemberIds_OrdinaryFirstName_IsNotARoster(string? firstName)
    {
        var parsed = RelayRosterName.TryParseMemberIds(firstName, out var memberIds);

        Assert.False(parsed);
        Assert.Empty(memberIds);
    }
}
