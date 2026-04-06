using System.Collections.Concurrent;

namespace WebLynx2;

/// <summary>
/// Thread-safe in-memory key-value store (same role as WebLynx <c>KeyValueStoreService</c>).
/// </summary>
public class KeyValueStoreService
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);

    /// <summary>
    /// Sets a key-value pair. If value is null or whitespace, removes the key.
    /// </summary>
    public void SetValue(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            _store.TryRemove(key, out _);
        else
            _store.AddOrUpdate(key, value, (_, _) => value);
    }

    public string? GetValue(string key) =>
        _store.TryGetValue(key, out var value) ? value : null;

    public Dictionary<string, string> GetAllValues() => new(_store);

    public bool HasKey(string key) => _store.ContainsKey(key);

    public bool RemoveKey(string key) => _store.TryRemove(key, out _);

    /// <summary>
    /// Removes all entries (e.g. before replacing from the UI).
    /// </summary>
    public void Clear() => _store.Clear();
}
