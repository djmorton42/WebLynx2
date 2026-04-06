using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.Utilities;

namespace WebLynx2.Parsing;

/// <summary>
/// Parses StartList header blocks and racer rows from timing software output.
/// </summary>
public class StartListMessageParser
{
    private readonly ILogger<StartListMessageParser> _logger;
    private readonly LapCountParser _lapCountParser;

    public StartListMessageParser(ILogger<StartListMessageParser> logger, LapCountParser lapCountParser)
    {
        _logger = logger;
        _lapCountParser = lapCountParser;
    }

    public RaceEvent? ParseHeader(string text)
    {
        try
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var eventData = new RaceEvent();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("OFFICIAL/UNOFFICIAL:"))
                {
                    eventData.IsOfficial = trimmedLine.Contains("OFFICIAL");
                }
                else if (trimmedLine.StartsWith("Event name"))
                {
                    eventData.EventName = ExtractValue(trimmedLine);
                }
                else if (trimmedLine.StartsWith("Wind"))
                {
                    eventData.Wind = ExtractValue(trimmedLine);
                }
                else if (trimmedLine.StartsWith("Event number"))
                {
                    eventData.EventNumber = ExtractValue(trimmedLine);
                }
                else if (trimmedLine.StartsWith("Round number"))
                {
                    eventData.RoundNumber = int.Parse(ExtractValue(trimmedLine));
                }
                else if (trimmedLine.StartsWith("Heat number"))
                {
                    eventData.HeatNumber = int.Parse(ExtractValue(trimmedLine));
                }
                else if (trimmedLine.StartsWith("EEE-R-HH Name"))
                {
                    eventData.EeeRhhName = ExtractValue(trimmedLine);
                }
                else if (trimmedLine.StartsWith("AUTO/MANUAL start"))
                {
                    eventData.StartType = ExtractValue(trimmedLine).ToUpperInvariant() == "AUTO"
                        ? StartType.Auto
                        : StartType.Manual;
                }
                else if (trimmedLine.StartsWith("Number of results"))
                {
                    eventData.NumberOfResults = int.Parse(ExtractValue(trimmedLine));
                }
            }

            return eventData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing StartListHeader from: {Text}", text);
            return null;
        }
    }

    public List<Racer> ParseRacers(string text)
    {
        var racers = new List<Racer>();

        try
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var inRacerSection = false;

            _logger.LogDebug("Parsing StartList with {LineCount} lines", lines.Length);

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Contains("Ln") && trimmedLine.Contains("Id") && trimmedLine.Contains("Name"))
                {
                    inRacerSection = true;
                    _logger.LogDebug("Found racer section header: {Header}", trimmedLine);
                    continue;
                }

                if (trimmedLine.StartsWith("---"))
                    continue;

                if (trimmedLine.StartsWith("*** StartList/Started/ResultsTrailer ***"))
                {
                    _logger.LogDebug("Found trailer, stopping racer parsing");
                    break;
                }

                if (inRacerSection && !string.IsNullOrWhiteSpace(trimmedLine))
                {
                    var racer = ParseRacerLine(trimmedLine);
                    if (racer is not null)
                    {
                        racers.Add(racer);
                        _logger.LogInformation("Added racer: Lane {Lane}, ID {Id}, Name {Name}", racer.Lane, racer.Id, racer.Name);
                    }
                }
            }

            _logger.LogDebug("Parsed {RacerCount} racers from StartList", racers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing racers from StartList: {Text}", text);
        }

        return racers;
    }

    public Racer? ParseRacerLine(string line)
    {
        try
        {
            if (line.Length >= 9)
            {
                var lane = FixedWidthParser.TrimParse(line, 0, 3, int.Parse);
                var id = FixedWidthParser.TrimParse(line, 4, 4, int.Parse);
                var name = FixedWidthParser.TrimParse(line, 9, 50, "Unknown");
                var affiliation = FixedWidthParser.TrimParse(line, 60, 30, s => s.TrimEnd('"'), "Unknown");
                var lapsText = FixedWidthParser.TrimParse(line, 91, 6, "");
                var laps = _lapCountParser.Parse(lapsText ?? "");

                _logger.LogDebug(
                    "Parsed racer: Lane={Lane}, ID={Id}, Name='{Name}', Affiliation='{Affiliation}'",
                    lane, id, name, affiliation);

                return new Racer
                {
                    Lane = lane,
                    Id = id,
                    Name = name ?? string.Empty,
                    Affiliation = affiliation ?? string.Empty,
                    LapsRemaining = laps
                };
            }

            _logger.LogDebug("Line too short for racer parsing: {Length} chars", line.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing racer from StartList line: {Line}", line);
        }

        return null;
    }

    private static string ExtractValue(string line)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex >= 0 && colonIndex < line.Length - 1)
            return line[(colonIndex + 1)..].Trim();

        return string.Empty;
    }
}
