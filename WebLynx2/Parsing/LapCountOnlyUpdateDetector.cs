using WebLynx2.Models;

namespace WebLynx2.Parsing;

/// <summary>
/// Detects Started-header updates that only carry lap counts (no place or split data).
/// </summary>
public static class LapCountOnlyUpdateDetector
{
    public static bool IsLapCountOnlyUpdate(IReadOnlyList<Racer> racers)
    {
        return racers.All(r =>
            !r.Place.HasPlaceData &&
            r.ReactionTime == null &&
            r.CumulativeSplitTime == null &&
            r.LastSplitTime == null &&
            r.BestSplitTime == null &&
            r.LapsRemaining > 0);
    }
}
