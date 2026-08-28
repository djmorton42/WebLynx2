namespace WebLynx2.UnofficialResults;

public class UnofficialRaceResult
{
    public string RaceNumber { get; set; } = string.Empty;
    public int Heat { get; set; }
    public int Round { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public DateTime? RaceStartTime { get; set; }
    public List<UnofficialRacerResult> Racers { get; set; } = new();
    public string FilePath { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}

public class UnofficialRacerResult
{
    public string Position { get; set; } = string.Empty;
    public int RacerId { get; set; }
    public int LineNumber { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Affiliation { get; set; } = string.Empty;
    public string FinishTimeRaw { get; set; } = string.Empty;
    public string FinishTimeFormatted { get; set; } = string.Empty;
    public TimeSpan? FinishTime { get; set; }
    public string DeltaTime { get; set; } = string.Empty;
    public string ReactionTime { get; set; } = string.Empty;
    public bool HasFinishTime { get; set; }
    public bool IsSpecialPosition { get; set; }
}

public class UnofficialRaceInfo
{
    public string RaceNumber { get; set; } = string.Empty;
    public int Heat { get; set; }
    public int Round { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public DateTime? RaceStartTime { get; set; }
    public int RacerCount { get; set; }
}
