namespace WebLynx2;

/// <summary>
/// Runtime override for the announcement message published on <c>/api/race/race-data</c>.
/// FinishLynx announcements continue to be stored on race state; when an override is active,
/// the API returns the forced text instead.
/// </summary>
public sealed class AnnouncementOverrideService
{
    private readonly object _gate = new();
    private string _forcedMessage = string.Empty;
    private bool _isActive;

    /// <summary>True after <see cref="Apply"/> until <see cref="Clear"/>.</summary>
    public bool IsActive
    {
        get
        {
            lock (_gate)
                return _isActive;
        }
    }

    /// <summary>Forced text when active; otherwise null.</summary>
    public string? ForcedMessage
    {
        get
        {
            lock (_gate)
                return _isActive ? _forcedMessage : null;
        }
    }

    /// <summary>Activates the override with the given message (empty string suppresses FinishLynx text).</summary>
    public void Apply(string? message)
    {
        lock (_gate)
        {
            _forcedMessage = message ?? string.Empty;
            _isActive = true;
        }
    }

    /// <summary>Removes the override so FinishLynx announcements are published again.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _forcedMessage = string.Empty;
            _isActive = false;
        }
    }

    /// <summary>
    /// Returns the forced message when active; otherwise <paramref name="finishLynxMessage"/>.
    /// </summary>
    public string? Resolve(string? finishLynxMessage)
    {
        lock (_gate)
            return _isActive ? _forcedMessage : finishLynxMessage;
    }
}
