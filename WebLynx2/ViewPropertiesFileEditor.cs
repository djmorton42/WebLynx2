namespace WebLynx2;

/// <summary>
/// Updates a single <c>view.properties</c> file on disk (UTF-8), preserving comments and unrelated lines.
/// </summary>
public static class ViewPropertiesFileEditor
{
    public static void Upsert(string filePath, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        List<string> lines;
        if (File.Exists(filePath))
            lines = File.ReadAllLines(filePath).ToList();
        else
            lines = new List<string>();

        var keyPrefix = key + "=";
        var found = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('#') || string.IsNullOrEmpty(t))
                continue;

            var eq = t.IndexOf('=');
            if (eq <= 0)
                continue;

            var lineKey = t[..eq].Trim();
            if (!string.Equals(lineKey, key, StringComparison.Ordinal))
                continue;

            lines[i] = key + "=" + value;
            found = true;
            break;
        }

        if (!found)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);
            lines.Add(key + "=" + value);
        }

        File.WriteAllLines(filePath, lines);
    }

    public static void RemoveKey(string filePath, string key)
    {
        if (!File.Exists(filePath))
            return;

        var lines = File.ReadAllLines(filePath).ToList();
        var kept = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith('#') || string.IsNullOrEmpty(t))
            {
                kept.Add(line);
                continue;
            }

            var eq = t.IndexOf('=');
            if (eq <= 0)
            {
                kept.Add(line);
                continue;
            }

            var lineKey = t[..eq].Trim();
            if (string.Equals(lineKey, key, StringComparison.Ordinal))
                continue;

            kept.Add(line);
        }

        File.WriteAllLines(filePath, kept);
    }
}
