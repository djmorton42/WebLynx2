using System.ComponentModel;
using System.Runtime.CompilerServices;
using WebLynx2.Models;

namespace WebLynx2;

public sealed class ViewPropertyRow : INotifyPropertyChanged
{
    private string _key;
    private string _value;

    /// <summary>Key as it appeared when loaded from disk; empty for rows added in the UI.</summary>
    public string InitialKey { get; }

    /// <summary>Files that contained <see cref="InitialKey"/> at load time; empty for new rows (saved to shared <c>view.properties</c> only).</summary>
    public IReadOnlyList<PropertySource> InitialSources { get; }

    public ViewPropertyRow(string initialKey, string initialValue, IReadOnlyList<PropertySource>? initialSources)
    {
        InitialKey = initialKey;
        InitialSources = initialSources is { Count: > 0 }
            ? initialSources.ToArray()
            : Array.Empty<PropertySource>();
        _key = initialKey;
        _value = initialValue;
    }

    public string SourcesSummary =>
        InitialSources.Count == 0
            ? "—"
            : string.Join(" · ", InitialSources.Select(s => s.DisplayName));

    public string Key
    {
        get => _key;
        set
        {
            if (_key == value)
                return;
            _key = value;
            OnPropertyChanged();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
