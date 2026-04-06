using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.Utilities;

namespace WebLynx2.Parsing;

/// <summary>
/// Parses Results header blocks and final placement rows.
/// </summary>
public class ResultsMessageParser
{
    private readonly ILogger<ResultsMessageParser> _logger;

    public ResultsMessageParser(ILogger<ResultsMessageParser> logger)
    {
        _logger = logger;
    }

    public List<Racer> ParseRacers(string text)
    {
        var racers = new List<Racer>();

        try
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var inRacerSection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("Plc Ln  Id") && trimmedLine.Contains("Name"))
                {
                    inRacerSection = true;
                    continue;
                }

                if (trimmedLine.StartsWith("---"))
                    continue;

                if (trimmedLine.StartsWith("*** StartList/Started/ResultsTrailer ***"))
                    break;

                if (inRacerSection && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    var racer = ParseRacerLine(line);
                    if (racer is not null)
                        racers.Add(racer);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing racers from Results: {Text}", text);
        }

        return racers;
    }

    public Racer? ParseRacerLine(string line)
    {
        try
        {
            if (line.Length >= 60)
            {
                var placeText = FixedWidthParser.TrimParse(line, 0, 3, string.Empty);
                var placeData = new PlaceData(placeText);
                if (!placeData.HasPlaceData)
                    return null;

                var lane = FixedWidthParser.TrimParse(line, 4, 3, int.Parse, 0);
                var id = FixedWidthParser.TrimParse(line, 8, 4, int.Parse, 0);
                var name = FixedWidthParser.TrimParse(line, 13, 50, "Unknown");
                var affiliation = FixedWidthParser.TrimParse(line, 64, 30, s => s.TrimEnd('"'), "Unknown");

                TimeSpan? finalTime = null;
                TimeSpan? deltaTime = null;
                TimeSpan? reactionTime = null;

                var times = FixedWidthParser.TrimParse(line, 95, 26, string.Empty);
                var parts = times?.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                if (parts.Length >= 1)
                    finalTime = TimeSpanParser.Parse(parts[0]);
                if (parts.Length >= 2)
                    deltaTime = TimeSpanParser.Parse(parts[1]);
                if (parts.Length >= 3)
                    reactionTime = TimeSpanParser.Parse(parts[2]);

                return new Racer
                {
                    Place = placeData,
                    Lane = lane,
                    Id = id,
                    Name = name ?? string.Empty,
                    Affiliation = affiliation ?? string.Empty,
                    FinalTime = finalTime,
                    DeltaTime = deltaTime,
                    ReactionTime = reactionTime,
                    HasFinished = true
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing racer from Results line: {Line}", line);
        }

        return null;
    }
}
