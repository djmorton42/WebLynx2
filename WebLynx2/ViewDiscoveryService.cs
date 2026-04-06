using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebLynx2.Models;

namespace WebLynx2;

/// <summary>
/// Scans a Views root directory for subfolders that match the WebLynx layout (each view is a folder with required <c>template.html</c>).
/// Loads <c>view.properties</c> from the Views root first (shared defaults), then each valid view's own <c>view.properties</c> (overrides).
/// </summary>
public class ViewDiscoveryService
{
    private readonly ILogger<ViewDiscoveryService> _logger;
    private readonly string _viewsPath;
    private readonly KeyValueStoreService? _keyValueStore;
    private readonly List<ViewMetadata> _discoveredViews = new();

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

        if (!Directory.Exists(_viewsPath))
        {
            _logger.LogWarning("Views directory not found: {ViewsPath}", _viewsPath);
            return;
        }

        var keyValueHistory = new Dictionary<string, (string firstSource, string firstValue)>(StringComparer.Ordinal);

        var sharedPropertiesPath = Path.Combine(_viewsPath, "view.properties");
        LoadPropertiesFile(sharedPropertiesPath, "Views directory (shared)", keyValueHistory);

        var viewDirectories = Directory.GetDirectories(_viewsPath)
            .Where(dir => !Path.GetFileName(dir).StartsWith('.'))
            .OrderBy(dir => Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase);

        foreach (var viewDirectory in viewDirectories)
        {
            var viewName = Path.GetFileName(viewDirectory);
            var viewMetadata = ValidateViewDirectory(viewDirectory, viewName);
            _discoveredViews.Add(viewMetadata);

            if (viewMetadata.IsValid)
            {
                _logger.LogInformation("Discovered valid view: {ViewName}", viewName);
                LoadViewProperties(viewDirectory, viewName, keyValueHistory);
            }
            else
            {
                _logger.LogWarning(
                    "Invalid view directory: {ViewName}. Missing files: {MissingFiles}",
                    viewName,
                    string.Join(", ", viewMetadata.MissingFiles));
            }
        }

        _logger.LogInformation(
            "View discovery completed. Found {ValidCount} valid views out of {TotalCount} directories",
            _discoveredViews.Count(v => v.IsValid),
            _discoveredViews.Count);
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

    private void LoadViewProperties(
        string viewDirectory,
        string viewName,
        Dictionary<string, (string firstSource, string firstValue)> keyValueHistory)
    {
        LoadPropertiesFile(Path.Combine(viewDirectory, "view.properties"), $"view '{viewName}'", keyValueHistory);
    }

    private void LoadPropertiesFile(
        string propertiesFilePath,
        string sourceLabel,
        Dictionary<string, (string firstSource, string firstValue)> keyValueHistory)
    {
        if (_keyValueStore is null || !File.Exists(propertiesFilePath))
            return;

        try
        {
            var lines = File.ReadAllLines(propertiesFilePath);
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                    continue;

                var equalIndex = trimmedLine.IndexOf('=');
                if (equalIndex <= 0 || equalIndex >= trimmedLine.Length - 1)
                {
                    _logger.LogWarning(
                        "Invalid line format in view.properties ({Source}): {Line}",
                        sourceLabel,
                        trimmedLine);
                    continue;
                }

                var key = trimmedLine[..equalIndex].Trim();
                var value = trimmedLine[(equalIndex + 1)..].Trim();

                if (string.IsNullOrEmpty(key))
                {
                    _logger.LogWarning(
                        "Empty key in view.properties ({Source}): {Line}",
                        sourceLabel,
                        trimmedLine);
                    continue;
                }

                if (keyValueHistory.TryGetValue(key, out var history))
                {
                    if (history.firstValue != value)
                    {
                        _logger.LogWarning(
                            "Key-value conflict for key '{Key}': '{FirstSource}' set '{FirstValue}', '{CurrentSource}' set '{CurrentValue}' (using '{CurrentValue}' - last wins)",
                            key,
                            history.firstSource,
                            history.firstValue,
                            sourceLabel,
                            value,
                            value);
                    }
                }
                else
                {
                    keyValueHistory[key] = (sourceLabel, value);
                }

                _keyValueStore.SetValue(key, value);
                _logger.LogDebug("Loaded key-value from {Source}: {Key} = {Value}", sourceLabel, key, value);
            }

            _logger.LogInformation("Loaded view.properties from {Source}", sourceLabel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load view.properties from {Source}", sourceLabel);
        }
    }
}
