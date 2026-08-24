using System.Text.Json;

namespace WebLynx2.Api;

public static class RaceHttpJsonSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
