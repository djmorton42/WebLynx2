using WebLynx2.Models;

namespace WebLynx2.Api;

public class RaceDataApiMapper(KeyValueStoreService keyValueStore, int delayedDisplaySeconds)
{
    public RaceDataApiResponse Map(RaceData raceData, string sortBy)
    {
        var sortedRacers = RacerSorter.Sort(raceData.Racers, sortBy);

        return new RaceDataApiResponse
        {
            CurrentTime = raceData.CurrentTime,
            Event = raceData.Event,
            Status = raceData.Status,
            LastUpdated = raceData.LastUpdated,
            AnnouncementMessage = raceData.AnnouncementMessage,
            HalfLapModeEnabled = true,
            KeyValues = keyValueStore.GetAllValues(),
            ViewConfig = ViewConfigBuilder.FromFlatKeyValues(keyValueStore.GetAllValues()),
            Racers = sortedRacers.Select(MapRacer).ToList()
        };
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
