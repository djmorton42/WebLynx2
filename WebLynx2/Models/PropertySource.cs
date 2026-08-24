namespace WebLynx2.Models;

/// <summary>
/// Identifies a <c>view.properties</c> file and how it is labeled in the UI.
/// </summary>
public sealed record PropertySource(string DisplayName, string PropertiesFilePath);
