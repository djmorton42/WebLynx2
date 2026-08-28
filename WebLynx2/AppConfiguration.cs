using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace WebLynx2;

public static class AppConfiguration
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsFilePath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppSettings Load() => LoadFrom(SettingsFilePath);

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
