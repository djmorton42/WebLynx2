namespace WebLynx2.Models;

/// <summary>
/// One merged configuration key, the effective value (last file wins), and every file that defines it.
/// </summary>
public sealed class DiscoveredViewProperty
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required IReadOnlyList<PropertySource> Sources { get; init; }
}
