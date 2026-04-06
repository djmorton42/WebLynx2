using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace WebLynx2.Parsing;

/// <summary>
/// Parses "Running time: M:SS.d" style clock strings from live race feeds.
/// </summary>
public class RunningTimeParser
{
    private readonly ILogger<RunningTimeParser> _logger;

    public RunningTimeParser(ILogger<RunningTimeParser> logger)
    {
        _logger = logger;
    }

    public TimeSpan? Parse(string text)
    {
        try
        {
            var match = Regex.Match(text, @"Running time:\s*(\d+:)?(\d+\.\d+)");
            if (match.Success)
            {
                var minutes = match.Groups[1].Success
                    ? int.Parse(match.Groups[1].Value.TrimEnd(':'))
                    : 0;
                var seconds = double.Parse(match.Groups[2].Value);

                return TimeSpan.FromMinutes(minutes).Add(TimeSpan.FromSeconds(seconds));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing running time from: {Text}", text);
        }

        return null;
    }
}
