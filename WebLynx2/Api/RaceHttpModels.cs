using System.Net;
using System.Text;
using System.Text.Json;

namespace WebLynx2.Api;

public sealed class RaceHttpRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }

    /// <summary>Case-insensitive query parameters (first value wins).</summary>
    public IReadOnlyDictionary<string, string> Query { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? GetQuery(string key) =>
        Query.TryGetValue(key, out var value) ? value : null;
}

public sealed class RaceHttpResponse
{
    public required HttpStatusCode StatusCode { get; init; }
    public string? ContentType { get; init; }
    public byte[] Body { get; init; } = [];
    public string? Location { get; init; }
    public string? CacheControl { get; init; }

    public string BodyText => Encoding.UTF8.GetString(Body);

    public static RaceHttpResponse Empty(HttpStatusCode statusCode) =>
        new() { StatusCode = statusCode };

    public static RaceHttpResponse Redirect(string location) =>
        new()
        {
            StatusCode = HttpStatusCode.Redirect,
            Location = location
        };

    public static RaceHttpResponse Json(object payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(payload, RaceHttpJsonSerializer.Options);
        return Bytes(Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", statusCode);
    }

    public static RaceHttpResponse Text(
        string text,
        string contentType = "text/plain; charset=utf-8",
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        Bytes(Encoding.UTF8.GetBytes(text), contentType, statusCode);

    public static RaceHttpResponse Bytes(
        byte[] body,
        string contentType,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? cacheControl = null) =>
        new()
        {
            StatusCode = statusCode,
            ContentType = contentType,
            Body = body,
            CacheControl = cacheControl
        };

    public static RaceHttpResponse NotFound() => Empty(HttpStatusCode.NotFound);

    public static RaceHttpResponse InternalServerError() =>
        Text("Internal server error", statusCode: HttpStatusCode.InternalServerError);
}
