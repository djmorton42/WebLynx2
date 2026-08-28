using System.Collections.Concurrent;

namespace WebLynx2;

/// <summary>
/// Thread-safe in-memory key-value store (same role as WebLynx <c>KeyValueStoreService</c>).
/// </summary>
public class KeyValueStoreService
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);
    private long _version;

    /// <summary>
    /// Monotonic version bumped on any mutation. Used to cache derived structures
    /// (e.g. nested viewConfig) across frequent race-data polls.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Sets a key-value pair. If value is null or whitespace, removes the key.
    /// </summary>
    public void SetValue(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (_store.TryRemove(key, out _))
                Interlocked.Increment(ref _version);
        }
        else if (!_store.TryGetValue(key, out var existing) || !string.Equals(existing, value, StringComparison.Ordinal))
        {
            _store.AddOrUpdate(key, value, (_, _) => value);
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Sets a key-value pair, keeping empty strings (unlike <see cref="SetValue"/>).
    /// </summary>
    public void Put(string key, string value)
    {
        if (!_store.TryGetValue(key, out var existing) || !string.Equals(existing, value, StringComparison.Ordinal))
        {
            _store.AddOrUpdate(key, value, (_, _) => value);
            Interlocked.Increment(ref _version);
        }
    }

    public string? GetValue(string key) =>
        _store.TryGetValue(key, out var value) ? value : null;

    public Dictionary<string, string> GetAllValues() => new(_store);

    public bool HasKey(string key) => _store.ContainsKey(key);

    public bool RemoveKey(string key)
    {
        if (!_store.TryRemove(key, out _))
            return false;

        Interlocked.Increment(ref _version);
        return true;
    }

    /// <summary>
    /// Removes all entries (e.g. before replacing from the UI).
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        Interlocked.Increment(ref _version);
    }
}
