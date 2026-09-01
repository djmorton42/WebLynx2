using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace WebLynx2;

public static class AppConfiguration
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// File used by both <see cref="Load"/> and <see cref="Save"/>.
    /// When running from a build output (<c>bin/Debug/net9.0</c>), this is the
    /// project-source <c>appsettings.json</c> so a save is not overwritten by
    /// <c>CopyToOutputDirectory</c> on the next <c>dotnet run</c>. Published
    /// apps (no nearby <c>.csproj</c>) use the executable directory.
    /// </summary>
    public static string SettingsFilePath =>
        ResolveSettingsFilePath(AppContext.BaseDirectory);

    public static AppSettings Load() => LoadFrom(SettingsFilePath);

    /// <summary>
    /// Resolves the settings file for a given application base directory.
    /// If that directory lives under a project's <c>bin/</c> output (typical of
    /// <c>dotnet run</c>), the project's source <c>appsettings.json</c> is used.
    /// Otherwise the copy next to the executable is used (published apps).
    /// </summary>
    public static string ResolveSettingsFilePath(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var start = new DirectoryInfo(Path.GetFullPath(baseDirectory));
        for (var dir = start; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "appsettings.json");
            if (!File.Exists(candidate) || dir.GetFiles("*.csproj").Length == 0)
                continue;

            var relative = Path.GetRelativePath(dir.FullName, start.FullName);
            var firstSegment = relative.Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (string.Equals(firstSegment, "bin", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return Path.Combine(start.FullName, "appsettings.json");
    }

    public static AppSettings LoadFrom(string path)
    {
        var settings = new AppSettings();
        if (!File.Exists(path))
            return settings;

        new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build()
            .Bind(settings);
        return settings;
    }

    /// <summary>
    /// Writes settings to <see cref="SettingsFilePath"/> (the same file <see cref="Load"/> reads).
    /// </summary>
    public static void Save(AppSettings settings) => SaveTo(SettingsFilePath, settings);

    public static void SaveTo(string path, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, WriteOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}
