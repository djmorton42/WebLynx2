using System.Globalization;

namespace WebLynx2.Api;

/// <summary>
/// Expands flat dot-notation key-values (e.g. <c>laneColors.1</c>) into a nested
/// dictionary suitable for view JS (same shape as legacy <c>VIEW_CONFIG</c>).
/// </summary>
public static class ViewConfigBuilder
{
    public static Dictionary<string, object> FromFlatKeyValues(IReadOnlyDictionary<string, string> flat)
    {
        var root = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var (key, rawValue) in flat.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var segments = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
                continue;

            var value = CoerceValue(rawValue);
            Insert(root, segments, value);
        }

        return root;
    }

    private static void Insert(Dictionary<string, object> node, string[] segments, object value)
    {
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (!node.TryGetValue(segment, out var child) || child is not Dictionary<string, object> childDict)
            {
                childDict = new Dictionary<string, object>(StringComparer.Ordinal);
                node[segment] = childDict;
            }

            node = childDict;
        }

        node[segments[^1]] = value;
    }

    private static object CoerceValue(string? raw)
    {
        if (raw is null)
            return string.Empty;

        if (bool.TryParse(raw, out var boolValue))
            return boolValue;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return intValue;

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) &&
            raw.Contains('.') &&
            !raw.StartsWith('#'))
            return doubleValue;

        return raw;
    }
}
