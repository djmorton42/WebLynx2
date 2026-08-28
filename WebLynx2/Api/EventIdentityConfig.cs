namespace WebLynx2.Api;

/// <summary>
/// Event title/subtitle from the main Event settings UI, published into the same
/// key-value / viewConfig channel as other view properties.
/// </summary>
public static class EventIdentityConfig
{
    public const string MeetTitleKey = "meetTitle";
    public const string EventSubtitleKey = "eventSubtitle";

    /// <summary>
    /// Writes UI event identity into the store (source of truth for the API).
    /// Empty strings are kept so <c>viewConfig</c> always exposes the keys.
    /// </summary>
    public static void ApplyTo(KeyValueStoreService store, string? meetTitle, string? eventSubtitle)
    {
        store.Put(MeetTitleKey, meetTitle ?? string.Empty);
        store.Put(EventSubtitleKey, eventSubtitle ?? string.Empty);
    }
}
