using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WebLynx2;

/// <summary>
/// Reads typed entries from <c>view.yaml</c>; duplicate keys in one file keep the last value.
/// </summary>
public static class ViewPropertiesYamlParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IReadOnlyList<ViewPropertyFileEntry> ParseFile(string filePath, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var result = new Dictionary<string, ViewPropertyFileEntry>(StringComparer.Ordinal);

        if (!File.Exists(filePath))
            return Array.Empty<ViewPropertyFileEntry>();

        string yaml;
        try
        {
            yaml = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read view.yaml: {Path}", filePath);
            return Array.Empty<ViewPropertyFileEntry>();
        }

        if (string.IsNullOrWhiteSpace(yaml))
            return Array.Empty<ViewPropertyFileEntry>();

        ViewPropertiesYamlDocument? document;
        try
        {
            document = Deserializer.Deserialize<ViewPropertiesYamlDocument>(yaml);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not parse view.yaml: {Path}", filePath);
            return Array.Empty<ViewPropertyFileEntry>();
        }

        if (document?.Properties is null)
            return Array.Empty<ViewPropertyFileEntry>();

        foreach (var item in document.Properties)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Key))
            {
                logger.LogWarning("Skipping view.yaml entry with empty key in {Path}", filePath);
                continue;
            }

            var key = item.Key.Trim();
            if (!TryParseType(item.Type, out var type))
            {
                logger.LogWarning("Unknown type '{Type}' for key '{Key}' in {Path}; treating as string", item.Type, key, filePath);
                type = ViewPropertyType.String;
            }

            var value = NormalizeValue(item.Value, type);
            result[key] = new ViewPropertyFileEntry
            {
                Key = key,
                Value = value,
                Type = type
            };
        }

        return result.Values.ToList();
    }

    private static bool TryParseType(string? raw, out ViewPropertyType type)
    {
        type = ViewPropertyType.String;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Trim().ToLowerInvariant() switch
        {
            "string" => Set(ViewPropertyType.String, out type),
            "integer" or "int" => Set(ViewPropertyType.Integer, out type),
            "boolean" or "bool" => Set(ViewPropertyType.Boolean, out type),
            "color" => Set(ViewPropertyType.Color, out type),
            _ => false
        };
    }

    private static bool Set(ViewPropertyType value, out ViewPropertyType type)
    {
        type = value;
        return true;
    }

    private static string NormalizeValue(object? raw, ViewPropertyType type)
    {
        if (raw is null)
            return type switch
            {
                ViewPropertyType.Boolean => "false",
                ViewPropertyType.Integer => "0",
                _ => string.Empty
            };

        return type switch
        {
            ViewPropertyType.Boolean => raw switch
            {
                bool b => b ? "true" : "false",
                string s when bool.TryParse(s, out var b) => b ? "true" : "false",
                _ => "false"
            },
            ViewPropertyType.Integer => raw switch
            {
                int i => i.ToString(),
                long l => l.ToString(),
                string s when int.TryParse(s, out var i) => i.ToString(),
                _ => "0"
            },
            ViewPropertyType.Color => raw.ToString()?.Trim() ?? string.Empty,
            _ => raw.ToString()?.Trim() ?? string.Empty
        };
    }

    private sealed class ViewPropertiesYamlDocument
    {
        public List<ViewPropertyYamlItem?>? Properties { get; set; }
    }

    private sealed class ViewPropertyYamlItem
    {
        public string Key { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public object? Value { get; set; }
    }
}
