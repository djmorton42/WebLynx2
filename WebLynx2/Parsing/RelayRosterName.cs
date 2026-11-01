using System.Text.RegularExpressions;
using WebLynx2.Models;

namespace WebLynx2.Parsing;

/// <summary>
/// FinishLynx sends one name, "{first} {last}". Relay entries put pipe-delimited
/// skater ids in the first name, for example "|104|245| TEAM RED".
/// </summary>
public static partial class RelayRosterName
{
    [GeneratedRegex(@"^\|(?<ids>\d+(?:\|\d+)*)\|\s+(?<team>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CombinedNamePattern();

    [GeneratedRegex(@"^\|(?<ids>\d+(?:\|\d+)*)\|$", RegexOptions.CultureInvariant)]
    private static partial Regex MemberIdPattern();

    public static bool TrySplit(string? combinedName, out string teamName, out IReadOnlyList<string> memberIds)
    {
        teamName = combinedName ?? "";
        memberIds = [];
        if (string.IsNullOrWhiteSpace(combinedName))
            return false;

        var match = CombinedNamePattern().Match(combinedName.Trim());
        if (!match.Success)
            return false;

        var team = match.Groups["team"].Value.Trim();
        if (team.Length == 0)
            return false;

        teamName = team;
        memberIds = SplitIds(match.Groups["ids"].Value);
        return true;
    }

    public static bool TryParseMemberIds(string? firstName, out IReadOnlyList<string> memberIds)
    {
        memberIds = [];
        if (string.IsNullOrWhiteSpace(firstName))
            return false;

        var match = MemberIdPattern().Match(firstName.Trim());
        if (!match.Success)
            return false;

        memberIds = SplitIds(match.Groups["ids"].Value);
        return memberIds.Count > 0;
    }

    public static void ApplyCombinedName(Racer racer, string? rawName)
    {
        if (TrySplit(rawName, out var teamName, out var memberIds))
        {
            racer.Name = teamName;
            racer.RelayMemberIds = memberIds.ToList();
            return;
        }

        racer.Name = rawName ?? "";
        racer.RelayMemberIds = [];
    }

    private static string[] SplitIds(string ids) => ids.Split('|');
}
