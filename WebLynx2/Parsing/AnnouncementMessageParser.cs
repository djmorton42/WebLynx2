using Microsoft.Extensions.Logging;

namespace WebLynx2.Parsing;

/// <summary>
/// Extracts user-visible text from Message Header / Message Trailer announcement blocks.
/// </summary>
public class AnnouncementMessageParser
{
    private readonly ILogger<AnnouncementMessageParser> _logger;

    public AnnouncementMessageParser(ILogger<AnnouncementMessageParser> logger)
    {
        _logger = logger;
    }

    public string? Parse(string text)
    {
        try
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var messageLines = new List<string>();
            var inMessage = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Contains("Message Header"))
                {
                    inMessage = true;
                    continue;
                }

                if (trimmedLine.Contains("Message Trailer"))
                    break;

                if (inMessage && !string.IsNullOrWhiteSpace(trimmedLine))
                    messageLines.Add(trimmedLine);
            }

            if (messageLines.Count > 0)
            {
                var message = string.Join(" ", messageLines);
                _logger.LogInformation("Parsed announcement message: {Message}", message);
                return message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing announcement message from: {Text}", text);
        }

        return null;
    }
}
