using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebLynx2;

/// <summary>
/// Reads <c>view.properties</c> lines; duplicate keys in one file keep the last value.
/// </summary>
public static class ViewPropertiesFileParser
{
    public static Dictionary<string, string> ParseFile(string filePath, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!File.Exists(filePath))
            return result;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read view.properties: {Path}", filePath);
            return result;
        }

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                continue;

            var equalIndex = trimmedLine.IndexOf('=');
            if (equalIndex <= 0 || equalIndex >= trimmedLine.Length - 1)
            {
                logger.LogWarning("Invalid line in view.properties ({Path}): {Line}", filePath, trimmedLine);
                continue;
            }

            var key = trimmedLine[..equalIndex].Trim();
            var value = trimmedLine[(equalIndex + 1)..].Trim();

            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("Empty key in view.properties ({Path}): {Line}", filePath, trimmedLine);
                continue;
            }

            result[key] = value;
        }

        return result;
    }
}
