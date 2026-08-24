using WebLynx2.Models;

namespace WebLynx2.Api;

public static class RacerSorter
{
    public static List<Racer> Sort(List<Racer> racers, string sortBy) =>
        sortBy.ToLowerInvariant() switch
        {
            "lane" => racers.OrderBy(r => r.Lane).ToList(),
            "place" => racers.OrderBy(r => r.Place).ThenBy(r => r.Lane).ToList(),
            _ => racers.OrderBy(r => r.Place).ThenBy(r => r.Lane).ToList()
        };
}
