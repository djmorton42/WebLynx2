using WebLynx2.Models;

namespace WebLynx2.Api;

public class RaceDataApiMapper(KeyValueStoreService keyValueStore, int delayedDisplaySeconds)
{
    private readonly object _viewConfigGate = new();
    private long _cachedViewConfigVersion = -1;
    private Dictionary<string, string> _cachedKeyValues = new();
    private Dictionary<string, object> _cachedViewConfig = new();

    public RaceDataApiResponse Map(RaceData raceData, string sortBy)
    {
        var sortedRacers = RacerSorter.Sort(raceData.Racers, sortBy);
        var (keyValues, viewConfig) = GetKeyValuesAndViewConfig();

        return new RaceDataApiResponse
        {
            CurrentTime = raceData.CurrentTime,
            Event = raceData.Event,
            Status = raceData.Status,
            LastUpdated = raceData.LastUpdated,
            AnnouncementMessage = raceData.AnnouncementMessage,
            HalfLapModeEnabled = true,
            KeyValues = keyValues,
            ViewConfig = viewConfig,
            Racers = sortedRacers.Select(MapRacer).ToList()
        };
    }

    private (Dictionary<string, string> KeyValues, Dictionary<string, object> ViewConfig) GetKeyValuesAndViewConfig()
    {
        var version = keyValueStore.Version;
        lock (_viewConfigGate)
        {
            if (version == _cachedViewConfigVersion)
                return (_cachedKeyValues, _cachedViewConfig);

            var keyValues = keyValueStore.GetAllValues();
            _cachedKeyValues = keyValues;
            _cachedViewConfig = ViewConfigBuilder.FromFlatKeyValues(keyValues);
            _cachedViewConfigVersion = version;
            return (_cachedKeyValues, _cachedViewConfig);
        }
    }

    private RacerApiResponse MapRacer(Racer racer) =>
        new()
        {
            Lane = racer.Lane,
            Id = racer.Id,
            Name = racer.Name,
            Affiliation = racer.Affiliation,
            PlaceText = racer.Place.PlaceText,
            HasPlaceData = racer.Place.HasPlaceData,
            ReactionTime = racer.ReactionTime,
            CumulativeSplitTime = racer.CumulativeSplitTime,
            LastSplitTime = racer.LastSplitTime,
            BestSplitTime = racer.BestSplitTime,
            LapsRemaining = racer.LapsRemaining,
            DelayedLapsRemaining = racer.GetDelayedLapsRemaining(delayedDisplaySeconds),
            LapCountLastChanged = racer.LapCountLastChanged,
            Speed = racer.Speed,
            Pace = racer.Pace,
            FinalTime = racer.FinalTime,
            DeltaTime = racer.DeltaTime,
            HasFinished = racer.HasFinished,
            HasFirstCrossing = racer.CumulativeSplitTime.HasValue || racer.LastSplitTime.HasValue
        };
}
