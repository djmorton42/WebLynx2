namespace WebLynx2.Models;

public class RaceData
{
    public RaceEvent? Event { get; set; }
    public List<Racer> Racers { get; set; } = new();
    public TimeSpan? CurrentTime { get; set; }
    public RaceStatus Status { get; set; } = RaceStatus.NotStarted;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string? AnnouncementMessage { get; set; }
}

public class RaceEvent
{
    public string EventName { get; set; } = string.Empty;
    public string Wind { get; set; } = string.Empty;
    public string EventNumber { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int HeatNumber { get; set; }
    public string EeeRhhName { get; set; } = string.Empty;
    public StartType StartType { get; set; }
    public bool IsOfficial { get; set; }
    public int NumberOfResults { get; set; }
}

public class PlaceData : IComparable<PlaceData>
{
    private readonly string _placeText;

    public PlaceData()
    {
        _placeText = string.Empty;
    }

    public PlaceData(string? placeText)
    {
        _placeText = placeText == null ? string.Empty : placeText.Trim();
    }

    public bool HasPlaceData => _placeText != string.Empty;

    public string PlaceText => _placeText;

    public int CompareTo(PlaceData? other)
    {
        if (other is null)
            return 1;

        var thisPriority = GetSortPriority(_placeText);
        var otherPriority = GetSortPriority(other._placeText);

        if (thisPriority != otherPriority)
            return thisPriority.CompareTo(otherPriority);

        return CompareWithinPriority(_placeText, other._placeText, thisPriority);
    }

    private static int GetSortPriority(string placeText)
    {
        if (int.TryParse(placeText, out var value) && value > 0)
            return 1;
        if (string.IsNullOrEmpty(placeText))
            return 2;
        return 3;
    }

    private static int CompareWithinPriority(string thisText, string otherText, int priority)
    {
        return priority switch
        {
            1 => int.Parse(thisText).CompareTo(int.Parse(otherText)),
            2 => 0,
            3 => string.Compare(thisText, otherText, StringComparison.Ordinal),
            _ => 0
        };
    }
}

public class Racer
{
    private decimal _lapsRemaining;
    private decimal _delayedLapsRemaining;
    private DateTime? _lapCountLastChanged;

    public int Lane { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Affiliation { get; set; } = string.Empty;
    public PlaceData Place { get; set; } = new();
    public TimeSpan? ReactionTime { get; set; }
    public TimeSpan? CumulativeSplitTime { get; set; }
    public TimeSpan? LastSplitTime { get; set; }
    public TimeSpan? BestSplitTime { get; set; }

    public decimal LapsRemaining
    {
        get => _lapsRemaining;
        set
        {
            if (_lapsRemaining == value)
                return;

            _delayedLapsRemaining = _lapsRemaining;
            _lapCountLastChanged = DateTime.UtcNow;
            _lapsRemaining = value;

            if (_delayedLapsRemaining == 0 && value > 0)
                _delayedLapsRemaining = value;
        }
    }

    public decimal DelayedLapsRemaining
    {
        get => _delayedLapsRemaining;
        set
        {
            _delayedLapsRemaining = value;
            _lapCountLastChanged = DateTime.UtcNow;
        }
    }

    public DateTime? LapCountLastChanged
    {
        get => _lapCountLastChanged;
        set => _lapCountLastChanged = value;
    }

    public decimal? Speed { get; set; }
    public decimal? Pace { get; set; }
    public TimeSpan? FinalTime { get; set; }
    public TimeSpan? DeltaTime { get; set; }
    public bool HasFinished { get; set; }

    public decimal GetDelayedLapsRemaining(int delaySeconds = 5)
    {
        if (_lapsRemaining <= 0)
            return 0;

        if (_lapCountLastChanged is null)
            return _lapsRemaining;

        var timeSinceChange = DateTime.UtcNow - _lapCountLastChanged.Value;
        if (timeSinceChange.TotalSeconds >= delaySeconds)
            return _lapsRemaining;

        return _delayedLapsRemaining;
    }

    public void InitializeDelayedLapCount()
    {
        _delayedLapsRemaining = _lapsRemaining;
        _lapCountLastChanged = DateTime.UtcNow;

        if (_delayedLapsRemaining == 0 && _lapsRemaining > 0)
            _delayedLapsRemaining = _lapsRemaining;
    }

    public void UpdateLapsRemaining(decimal newValue, bool skipDelay = false)
    {
        if (skipDelay)
        {
            if (_lapsRemaining == newValue)
                return;

            _lapsRemaining = newValue;
            _delayedLapsRemaining = newValue;
            _lapCountLastChanged = DateTime.UtcNow;
        }
        else
        {
            LapsRemaining = newValue;
        }
    }
}

public enum RaceStatus
{
    NotStarted,
    Running,
    Paused,
    Finished
}

public enum StartType
{
    Auto,
    Manual
}

public enum MessageType
{
    RunningTime,
    StartListHeader,
    StartedHeader,
    ResultsHeader,
    Announcement,
    Unknown
}
