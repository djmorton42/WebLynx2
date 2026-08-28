using WebLynx2.Models;

namespace WebLynx2.Api;

public class RaceDataApiResponse
{
    public TimeSpan? CurrentTime { get; set; }
    public RaceEvent? Event { get; set; }
    public RaceStatus Status { get; set; }
    public DateTime LastUpdated { get; set; }
    public List<RacerApiResponse> Racers { get; set; } = new();
    public string? AnnouncementMessage { get; set; }
    public bool HalfLapModeEnabled { get; set; }
    public Dictionary<string, string> KeyValues { get; set; } = new();
    /// <summary>
    /// Nested view configuration expanded from <see cref="KeyValues"/> (dot-notation → objects).
    /// Same shape as legacy injected VIEW_CONFIG for ported HTML views.
    /// </summary>
    public Dictionary<string, object> ViewConfig { get; set; } = new();
}

public class RacerApiResponse
{
    public int Lane { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Affiliation { get; set; } = string.Empty;
    public string PlaceText { get; set; } = string.Empty;
    public bool HasPlaceData { get; set; }
    public TimeSpan? ReactionTime { get; set; }
    public TimeSpan? CumulativeSplitTime { get; set; }
    public TimeSpan? LastSplitTime { get; set; }
    public TimeSpan? BestSplitTime { get; set; }
    public decimal LapsRemaining { get; set; }
    public decimal DelayedLapsRemaining { get; set; }
    public DateTime? LapCountLastChanged { get; set; }
    public decimal? Speed { get; set; }
    public decimal? Pace { get; set; }
    public TimeSpan? FinalTime { get; set; }
    public TimeSpan? DeltaTime { get; set; }
    public bool HasFinished { get; set; }
    public bool HasFirstCrossing { get; set; }
}
