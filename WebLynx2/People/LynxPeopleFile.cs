using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using WebLynx2.Models;

namespace WebLynx2.People;

public sealed class LynxPerson
{
    public string Id { get; init; } = "";
    public string LastName { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string Affiliation { get; init; } = "";
}

public static class LynxPeopleFileParser
{
    public static Dictionary<string, LynxPerson> Parse(string text)
    {
        var people = new Dictionary<string, LynxPerson>(StringComparer.Ordinal);
        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim
        });

        while (csv.Read())
        {
            var id = csv.GetField(0)?.Trim() ?? "";
            if (id.Length == 0)
                continue;

            people[id] = new LynxPerson
            {
                Id = id,
                LastName = csv.GetField(1)?.Trim() ?? "",
                FirstName = csv.GetField(2)?.Trim() ?? "",
                Affiliation = csv.GetField(3)?.Trim() ?? ""
            };
        }

        return people;
    }
}

public static class RelayRoster
{
    public static List<RelayRosterMember> Resolve(
        IReadOnlyList<string>? memberIds,
        IReadOnlyDictionary<string, LynxPerson>? people)
    {
        var roster = new List<RelayRosterMember>();
        if (memberIds is null)
            return roster;

        foreach (var id in memberIds)
        {
            LynxPerson? person = null;
            people?.TryGetValue(id, out person);
            roster.Add(new RelayRosterMember
            {
                Id = id,
                FirstName = person?.FirstName ?? "",
                LastName = person?.LastName ?? ""
            });
        }

        return roster;
    }
}

/// <summary>
/// Cached FinishLynx <c>Lynx.ppl</c> index. Reloads only when the file's last write time changes.
/// </summary>
public sealed class LynxPeopleFile
{
    private readonly object _gate = new();
    private Dictionary<string, LynxPerson> _people = new(StringComparer.Ordinal);
    private DateTime? _loadedWriteTimeUtc;
    private string? _loadedPath;

    public string? FilePath { get; set; }

    public Encoding FileEncoding { get; set; } = Encoding.GetEncoding("ISO-8859-1");

    /// <summary>True when the in-memory index contents changed.</summary>
    public bool Refresh()
    {
        lock (_gate)
        {
            var path = FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                if (_people.Count == 0 && _loadedWriteTimeUtc is null)
                    return false;

                _people = new Dictionary<string, LynxPerson>(StringComparer.Ordinal);
                _loadedWriteTimeUtc = null;
                _loadedPath = null;
                return true;
            }

            DateTime writeTime;
            try
            {
                writeTime = File.GetLastWriteTimeUtc(path);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (_loadedWriteTimeUtc == writeTime &&
                string.Equals(_loadedPath, path, StringComparison.Ordinal))
                return false;

            string text;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, FileEncoding, detectEncodingFromByteOrderMarks: false);
                text = reader.ReadToEnd();
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            var parsed = LynxPeopleFileParser.Parse(text);
            var changed = !SamePeople(_people, parsed);
            _people = new Dictionary<string, LynxPerson>(parsed, StringComparer.Ordinal);
            _loadedWriteTimeUtc = writeTime;
            _loadedPath = path;
            return changed;
        }
    }

    public LynxPerson? Find(string id)
    {
        lock (_gate)
            return _people.TryGetValue(id, out var person) ? person : null;
    }

    public IReadOnlyDictionary<string, LynxPerson> Snapshot()
    {
        lock (_gate)
            return new Dictionary<string, LynxPerson>(_people, StringComparer.Ordinal);
    }

    private static bool SamePeople(
        Dictionary<string, LynxPerson> current,
        IReadOnlyDictionary<string, LynxPerson> next)
    {
        if (current.Count != next.Count)
            return false;

        foreach (var (id, person) in next)
        {
            if (!current.TryGetValue(id, out var existing))
                return false;
            if (!string.Equals(existing.FirstName, person.FirstName, StringComparison.Ordinal) ||
                !string.Equals(existing.LastName, person.LastName, StringComparison.Ordinal) ||
                !string.Equals(existing.Affiliation, person.Affiliation, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
