using WebLynx2.Models;

namespace WebLynx2;

public sealed class ViewPropertyFileEntry
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required ViewPropertyType Type { get; init; }
}
