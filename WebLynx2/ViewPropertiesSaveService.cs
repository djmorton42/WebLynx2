using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2.Models;

namespace WebLynx2;

/// <summary>
/// Writes the properties grid back to the same <c>view.yaml</c> files that supplied each key at load time.
/// </summary>
public static class ViewPropertiesSaveService
{
    public static void Save(
        string viewsRootPath,
        IReadOnlyList<ViewPropertyRow> rows,
        IReadOnlyDictionary<string, IReadOnlyList<string>> loadSnapshotKeyToPropertyFilePaths,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        viewsRootPath = Path.GetFullPath(viewsRootPath);
        var rootFile = Path.GetFullPath(Path.Combine(viewsRootPath, ViewPropertiesFiles.FileName));

        var current = new Dictionary<string, ViewPropertyRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key))
                continue;
            current[row.Key.Trim()] = row;
        }

        foreach (var kv in loadSnapshotKeyToPropertyFilePaths)
        {
            var oldKey = kv.Key;
            var paths = kv.Value;
            if (current.ContainsKey(oldKey))
                continue;

            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path))
                    continue;
                ViewPropertiesYamlFileEditor.RemoveKey(path, oldKey);
                logger.LogDebug("Removed key {Key} from {Path}", oldKey, path);
            }
        }

        foreach (var row in current.Values)
        {
            var k = row.Key.Trim();
            var v = row.Value;
            var type = row.Type;

            var renamedFromLoad =
                !string.IsNullOrEmpty(row.InitialKey) &&
                !string.Equals(row.InitialKey, k, StringComparison.Ordinal);

            if (renamedFromLoad)
            {
                foreach (var src in row.InitialSources)
                {
                    var p = src.PropertiesFilePath;
                    if (File.Exists(p))
                        ViewPropertiesYamlFileEditor.RemoveKey(p, row.InitialKey);
                }

                ViewPropertiesYamlFileEditor.Upsert(rootFile, k, v, type);
                logger.LogInformation("Renamed property {OldKey} -> {NewKey}; wrote to shared {Path}", row.InitialKey, k, rootFile);
                continue;
            }

            if (row.InitialSources.Count == 0)
            {
                ViewPropertiesYamlFileEditor.Upsert(rootFile, k, v, type);
                continue;
            }

            foreach (var src in row.InitialSources.DistinctBy(s => s.PropertiesFilePath))
            {
                var p = src.PropertiesFilePath;
                if (File.Exists(p))
                    ViewPropertiesYamlFileEditor.Upsert(p, k, v, type);
                else
                {
                    logger.LogWarning("Properties file missing, using shared file for key {Key}: {Path}", k, p);
                    ViewPropertiesYamlFileEditor.Upsert(rootFile, k, v, type);
                }
            }
        }
    }
}
