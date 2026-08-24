using WebLynx2.Models;

namespace WebLynx2.Tests.Helpers;

internal static class RaceTestDataFactory
{
    public static RaceData CreateSampleRace()
    {
        var lastUpdated = new DateTime(2024, 1, 15, 10, 30, 45, DateTimeKind.Utc);

        return new RaceData
        {
            CurrentTime = TimeSpan.FromSeconds(83.456789),
            Status = RaceStatus.Running,
            LastUpdated = lastUpdated,
            AnnouncementMessage = "Race in progress",
            Event = new RaceEvent
            {
                EventName = "Men's 1000m",
                EventNumber = "Event 1",
                Wind = "1.2",
                RoundNumber = 1,
                HeatNumber = 2,
                EeeRhhName = "1000m",
                StartType = StartType.Auto,
                IsOfficial = true,
                NumberOfResults = 4
            },
            Racers =
            [
                CreateRacer(
                    lane: 2,
                    id: 102,
                    name: "Jane Doe",
                    place: "2",
                    cumulativeSplit: TimeSpan.FromSeconds(80),
                    lapsRemaining: 8.5m),
                CreateRacer(
                    lane: 1,
                    id: 101,
                    name: "John Smith",
                    place: "1",
                    cumulativeSplit: TimeSpan.FromSeconds(78),
                    lapsRemaining: 8.5m),
                CreateRacer(
                    lane: 3,
                    id: 103,
                    name: "Pat Lee",
                    place: "",
                    cumulativeSplit: null,
                    lapsRemaining: 9m)
            ]
        };
    }

    public static Racer CreateRacer(
        int lane,
        int id,
        string name,
        string place,
        TimeSpan? cumulativeSplit,
        decimal lapsRemaining)
    {
        var racer = new Racer
        {
            Lane = lane,
            Id = id,
            Name = name,
            Affiliation = "Team Alpha",
            Place = new PlaceData(place),
            CumulativeSplitTime = cumulativeSplit,
            LastSplitTime = cumulativeSplit.HasValue ? TimeSpan.FromSeconds(15) : null,
            BestSplitTime = TimeSpan.FromSeconds(14.9876543),
            Speed = 45.2m,
            Pace = 80.1234567m,
            DeltaTime = TimeSpan.FromSeconds(2.1234567),
            HasFinished = false
        };

        racer.UpdateLapsRemaining(lapsRemaining, skipDelay: true);
        racer.LapCountLastChanged = DateTime.UtcNow.AddSeconds(-10);
        return racer;
    }

    public static List<Racer> CreateRacersForSorting()
    {
        return
        [
            CreateRacer(3, 3, "Lane Three", "3", null, 5m),
            CreateRacer(1, 1, "Lane One", "2", null, 5m),
            CreateRacer(2, 2, "Lane Two", "1", null, 5m)
        ];
    }
}
