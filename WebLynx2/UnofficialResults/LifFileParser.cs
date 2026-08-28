using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace WebLynx2.UnofficialResults;

/// <summary>
/// Parses FinishLynx LIF (CSV) content into unofficial race results.
/// Pure/string-based so unit tests do not need the filesystem.
/// </summary>
public static class LifFileParser
{
    private static readonly HashSet<string> SpecialPositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "DNF", "DNS", "DSQ", "DQ", "WD", "WDR", "SCR", "PEN"
    };

    public static UnofficialRaceResult? ParseLines(
        IReadOnlyList<string> lines,
        string filePath,
        DateTime lastUpdated)
    {
        var nonEmpty = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        if (nonEmpty.Count == 0)
            return null;

        var raceInfo = ParseRaceInfo(nonEmpty[0]);
        var raceResult = new UnofficialRaceResult
        {
            FilePath = filePath,
            LastUpdated = lastUpdated,
            RaceNumber = raceInfo.RaceNumber,
            Heat = raceInfo.Heat,
            Round = raceInfo.Round,
            EventName = raceInfo.EventName,
            StartTime = raceInfo.StartTime,
            RaceStartTime = ParseStartTime(raceInfo.StartTime)
        };

        for (var i = 1; i < nonEmpty.Count; i++)
        {
            var racer = ParseRacer(nonEmpty[i]);
            if (racer is not null)
                raceResult.Racers.Add(racer);
        }

        return raceResult;
    }

    public static UnofficialRaceResult? ParseText(string text, string filePath, DateTime lastUpdated)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        return ParseLines(lines, filePath, lastUpdated);
    }

    public static bool IsResultsComplete(UnofficialRaceResult raceResult) =>
        raceResult.Racers.Count > 0 &&
        raceResult.Racers.All(r => r.HasFinishTime || r.IsSpecialPosition);

    public static bool IsSpecialPosition(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
            return false;
        return SpecialPositions.Contains(position.Trim());
    }

    public static TimeSpan? ParseTimeToTimeSpan(string? timeString)
    {
        if (string.IsNullOrWhiteSpace(timeString))
            return null;

        if (double.TryParse(timeString, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawSeconds))
            return TimeSpan.FromSeconds(rawSeconds);

        if (timeString.Contains(':', StringComparison.Ordinal))
        {
            var parts = timeString.Split(':');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromMinutes(minutes).Add(TimeSpan.FromSeconds(seconds));
            }
        }

        return null;
    }

    public static string FormatTime(TimeSpan? time)
    {
        if (!time.HasValue)
            return string.Empty;

        var totalSeconds = time.Value.TotalSeconds;
        var minutes = (int)(totalSeconds / 60);
        var seconds = totalSeconds % 60;

        return minutes > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00.000}")
            : seconds.ToString("F3", CultureInfo.InvariantCulture);
    }

    public static DateTime? ParseStartTime(string? startTimeStr, DateTime? todayOverride = null)
    {
        if (string.IsNullOrWhiteSpace(startTimeStr))
            return null;

        if (TimeSpan.TryParse(startTimeStr, CultureInfo.InvariantCulture, out var timeSpan))
            return (todayOverride ?? DateTime.Today).Add(timeSpan);

        if (DateTime.TryParse(startTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            return dateTime;

        return null;
    }

    private static (string RaceNumber, int Heat, int Round, string EventName, string StartTime) ParseRaceInfo(string line)
    {
        using var reader = new StringReader(line);
        using var csv = new CsvReader(reader, CreateCsvConfig());

        if (!csv.Read())
            throw new InvalidOperationException("Failed to read race info line");

        var raceNumber = csv.GetField(0) ?? string.Empty;
        var heatStr = csv.GetField(1) ?? "0";
        var roundStr = csv.GetField(2) ?? "0";
        var eventName = csv.GetField(3) ?? string.Empty;
        var startTime = csv.GetField(10) ?? string.Empty;

        int.TryParse(heatStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var heat);
        int.TryParse(roundStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round);

        return (raceNumber, heat, round, eventName, startTime);
    }

    private static UnofficialRacerResult? ParseRacer(string line)
    {
        using var reader = new StringReader(line);
        using var csv = new CsvReader(reader, CreateCsvConfig());

        if (!csv.Read())
            return null;

        var position = csv.GetField(0) ?? string.Empty;
        var racerIdStr = csv.GetField(1) ?? "0";
        var lineNumberStr = csv.GetField(2) ?? "0";
        var lastName = csv.GetField(3) ?? string.Empty;
        var firstName = csv.GetField(4) ?? string.Empty;
        var affiliation = csv.GetField(5) ?? string.Empty;
        var finishTime = csv.GetField(6) ?? string.Empty;

        int.TryParse(racerIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var racerId);
        int.TryParse(lineNumberStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNumber);

        var racer = new UnofficialRacerResult
        {
            Position = position.Trim(),
            RacerId = racerId,
            LineNumber = lineNumber,
            LastName = lastName,
            FirstName = firstName,
            Name = $"{firstName} {lastName}".Trim(),
            Affiliation = affiliation,
            FinishTimeRaw = finishTime,
            IsSpecialPosition = IsSpecialPosition(position)
        };

        if (!string.IsNullOrWhiteSpace(finishTime))
        {
            racer.FinishTime = ParseTimeToTimeSpan(finishTime);
            racer.FinishTimeFormatted = FormatTime(racer.FinishTime);
            racer.HasFinishTime = racer.FinishTime.HasValue;
        }

        return racer;
    }

    private static CsvConfiguration CreateCsvConfig() =>
        new(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            MissingFieldFound = null
        };
}
