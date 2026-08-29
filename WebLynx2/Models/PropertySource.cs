namespace WebLynx2.Models;

/// <summary>
/// Identifies a <c>view.yaml</c> file and how it is labeled in the UI.
/// </summary>
public sealed record PropertySource(string DisplayName, string PropertiesFilePath);
