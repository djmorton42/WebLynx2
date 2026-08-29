using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2.Models;

namespace WebLynx2;

/// <summary>
/// Scans a Views root directory for subfolders that match the WebLynx layout (each view is a folder with required <c>template.html</c>).
/// Loads <c>view.yaml</c> from the Views root first (shared defaults), then each valid view's own <c>view.yaml</c> (overrides).
/// </summary>
public class ViewDiscoveryService
{
    private readonly ILogger<ViewDiscoveryService> _logger;
    private readonly string _viewsPath;
    private readonly KeyValueStoreService? _keyValueStore;
    private readonly List<ViewMetadata> _discoveredViews = new();
    private readonly List<DiscoveredViewProperty> _propertyCatalog = new();

    /// <summary>
    /// Effective merged properties and the list of <c>view.yaml</c> files that define each key.
    /// </summary>
    public IReadOnlyList<DiscoveredViewProperty> LastPropertyCatalog => _propertyCatalog;

    /// <summary>
    /// Resolves <paramref name="configured"/> as an absolute path: absolute paths are normalized;
    /// relative paths are rooted under <see cref="AppContext.BaseDirectory"/> (empty uses <c>Views</c>).
    /// </summary>
    public static string ResolveViewsRoot(string? configured)
    {
        var baseDir = AppContext.BaseDirectory;
        var d = (configured ?? "").Trim();
        if (string.IsNullOrEmpty(d))
            d = "Views";

        return Path.IsPathRooted(d)
            ? Path.GetFullPath(d)
            : Path.GetFullPath(Path.Combine(baseDir, d));
    }

    public ViewDiscoveryService(
        string viewsPath,
        ILogger<ViewDiscoveryService>? logger = null,
        KeyValueStoreService? keyValueStore = null)
    {
        _viewsPath = viewsPath;
        _logger = logger ?? NullLogger<ViewDiscoveryService>.Instance;
        _keyValueStore = keyValueStore;
    }

    public IReadOnlyList<ViewMetadata> DiscoveredViews => _discoveredViews;

    public void DiscoverViews()
    {
        _discoveredViews.Clear();
        _propertyCatalog.Clear();
        _keyValueStore?.Clear();

        if (!Directory.Exists(_viewsPath))
        {
            _logger.LogWarning("Views directory not found: {ViewsPath}", _viewsPath);
            return;
        }

        var viewDirectories = Directory.GetDirectories(_viewsPath)
            .Where(dir => !Path.GetFileName(dir).StartsWith('.'))
            .OrderBy(dir => Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase);

        foreach (var viewDirectory in viewDirectories)
        {
            var viewName = Path.GetFileName(viewDirectory);
            var viewMetadata = ValidateViewDirectory(viewDirectory, viewName);
            _discoveredViews.Add(viewMetadata);

            if (viewMetadata.IsValid)
                _logger.LogInformation("Discovered valid view: {ViewName}", viewName);
            else
            {
                _logger.LogWarning(
                    "Invalid view directory: {ViewName}. Missing files: {MissingFiles}",
                    viewName,
                    string.Join(", ", viewMetadata.MissingFiles));
            }
        }

        var accum = new Dictionary<string, List<(PropertySource Source, string Value, ViewPropertyType Type)>>(StringComparer.Ordinal);
        var keyValueHistory = new Dictionary<string, (string firstSource, string firstValue)>(StringComparer.Ordinal);

        var sharedPropertiesPath = Path.Combine(_viewsPath, ViewPropertiesFiles.FileName);
        MergePropertiesFile(sharedPropertiesPath, "Views directory (shared)", accum, keyValueHistory);

        foreach (var viewDirectory in viewDirectories)
        {
            var viewName = Path.GetFileName(viewDirectory);
            var meta = GetViewMetadata(viewName);
            if (meta is null || !meta.IsValid)
                continue;

            MergePropertiesFile(
                Path.Combine(viewDirectory, ViewPropertiesFiles.FileName),
                $"view '{viewName}'",
                accum,
                keyValueHistory);
        }

        BuildPropertyCatalog(accum);

        _logger.LogInformation(
            "View discovery completed. Found {ValidCount} valid views out of {TotalCount} directories; {PropCount} merged properties.",
            _discoveredViews.Count(v => v.IsValid),
            _discoveredViews.Count,
            _propertyCatalog.Count);
    }

    private void BuildPropertyCatalog(Dictionary<string, List<(PropertySource Source, string Value, ViewPropertyType Type)>> accum)
    {
        foreach (var key in accum.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var list = accum[key];
            var effective = list[^1];
            var sources = new List<PropertySource>();
            foreach (var (src, _, _) in list)
            {
                if (sources.All(s =>
                        !string.Equals(s.PropertiesFilePath, src.PropertiesFilePath, StringComparison.OrdinalIgnoreCase)))
                    sources.Add(src);
            }

            _propertyCatalog.Add(new DiscoveredViewProperty
            {
                Key = key,
                Value = effective.Value,
                Type = effective.Type,
                Sources = sources
            });

            _keyValueStore?.SetValue(key, effective.Value);
        }
    }

    private void MergePropertiesFile(
        string propertiesFilePath,
        string sourceLabel,
        Dictionary<string, List<(PropertySource Source, string Value, ViewPropertyType Type)>> accum,
        Dictionary<string, (string firstSource, string firstValue)> keyValueHistory)
    {
        if (!File.Exists(propertiesFilePath))
            return;

        var entries = ViewPropertiesYamlParser.ParseFile(propertiesFilePath, _logger);
        if (entries.Count == 0 && File.Exists(propertiesFilePath))
        {
            _logger.LogInformation("Loaded view.yaml from {Source} (no property entries)", sourceLabel);
            return;
        }

        var src = new PropertySource(sourceLabel, Path.GetFullPath(propertiesFilePath));

        foreach (var entry in entries)
        {
            if (keyValueHistory.TryGetValue(entry.Key, out var history))
            {
                if (history.firstValue != entry.Value)
                {
                    _logger.LogWarning(
                        "Key-value conflict for key '{Key}': '{FirstSource}' set '{FirstValue}', '{CurrentSource}' set '{CurrentValue}' (using '{CurrentValue}' - last wins)",
                        entry.Key,
                        history.firstSource,
                        history.firstValue,
                        sourceLabel,
                        entry.Value,
                        entry.Value);
                }
            }
            else
            {
                keyValueHistory[entry.Key] = (sourceLabel, entry.Value);
            }

            if (!accum.TryGetValue(entry.Key, out var list))
            {
                list = new List<(PropertySource Source, string Value, ViewPropertyType Type)>();
                accum[entry.Key] = list;
            }

            list.Add((src, entry.Value, entry.Type));
            _logger.LogDebug("Loaded key-value from {Source}: {Key} = {Value} ({Type})", sourceLabel, entry.Key, entry.Value, entry.Type);
        }

        _logger.LogInformation("Loaded view.yaml from {Source}", sourceLabel);
    }

    public ViewMetadata? GetViewMetadata(string viewName) =>
        _discoveredViews.FirstOrDefault(v => v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));

    public bool IsValidView(string viewName) => GetViewMetadata(viewName)?.IsValid ?? false;

    private ViewMetadata ValidateViewDirectory(string viewDirectory, string viewName)
    {
        var metadata = new ViewMetadata
        {
            Name = viewName,
            DisplayName = FormatDisplayName(viewName),
            TemplatePath = Path.Combine(viewDirectory, "template.html"),
            StylesPath = Path.Combine(viewDirectory, "styles.css")
        };

        metadata.RequiredFiles.Add("template.html");

        foreach (var requiredFile in metadata.RequiredFiles)
        {
            var filePath = Path.Combine(viewDirectory, requiredFile);
            if (!File.Exists(filePath))
                metadata.MissingFiles.Add(requiredFile);
        }

        foreach (var optionalFile in new[] { "styles.css" })
        {
            var filePath = Path.Combine(viewDirectory, optionalFile);
            if (File.Exists(filePath) && !metadata.RequiredFiles.Contains(optionalFile))
                metadata.RequiredFiles.Add(optionalFile);
        }

        metadata.Description = GetViewDescription(viewDirectory, viewName);
        metadata.IsValid = metadata.MissingFiles.Count == 0;

        return metadata;
    }

    private static string FormatDisplayName(string viewName)
    {
        var parts = viewName
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return viewName;

        return string.Join(
            " ",
            parts.Select(static w =>
                w.Length == 1
                    ? w.ToUpperInvariant()
                    : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
    }

    private string GetViewDescription(string viewDirectory, string viewName)
    {
        var descriptionFile = Path.Combine(viewDirectory, "description.txt");
        if (!File.Exists(descriptionFile))
            return string.Empty;

        try
        {
            return File.ReadAllText(descriptionFile).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read description file for view {ViewName}", viewName);
            return string.Empty;
        }
    }
}
