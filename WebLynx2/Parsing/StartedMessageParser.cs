using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.Utilities;

namespace WebLynx2.Parsing;

/// <summary>
/// Parses Started (in-race) header blocks and live progress rows.
/// </summary>
public class StartedMessageParser
{
    private readonly ILogger<StartedMessageParser> _logger;
    private readonly LapCountParser _lapCountParser;

    public StartedMessageParser(ILogger<StartedMessageParser> logger, LapCountParser lapCountParser)
    {
        _logger = logger;
        _lapCountParser = lapCountParser;
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

                if (trimmedLine.StartsWith("Plc Ln") && trimmedLine.Contains("ReacTime"))
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
            _logger.LogError(ex, "Error parsing racers from Started: {Text}", text);
        }

        _logger.LogInformation("Parsed {Count} racers from Started", racers.Count);
        return racers;
    }

    public Racer? ParseRacerLine(string line)
    {
        try
        {
            var placeText = FixedWidthParser.TrimParse(line, 0, 3, string.Empty);
            var place = new PlaceData(placeText);
            var lane = FixedWidthParser.TrimParse(line, 4, 3, int.Parse);
            var reactionTime = FixedWidthParser.TrimParse(line, 8, 8, TimeSpanParser.Parse);
            var cumulativeSplitTime = FixedWidthParser.TrimParse(line, 17, 8, TimeSpanParser.Parse);
            var lastSplitTime = FixedWidthParser.TrimParse(line, 26, 8, TimeSpanParser.Parse);
            var bestSplitTime = FixedWidthParser.TrimParse(line, 35, 8, TimeSpanParser.Parse);
            var lapsText = FixedWidthParser.TrimParse(line, 44, 6, "");
            var lapsRemaining = _lapCountParser.Parse(lapsText ?? "");

            var pace = FixedWidthParser.TrimParse(line, 58, 6, decimal.Parse, -1);
            var speed = FixedWidthParser.TrimParse(line, 51, 6, decimal.Parse, -1);

            return new Racer
            {
                Place = place,
                Lane = lane,
                ReactionTime = reactionTime,
                CumulativeSplitTime = cumulativeSplitTime,
                LastSplitTime = lastSplitTime,
                BestSplitTime = bestSplitTime,
                LapsRemaining = lapsRemaining,
                Speed = speed,
                Pace = pace
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing racer from Started line: {Line}", line);
        }

        return null;
    }
}
