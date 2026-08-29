using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using WebLynx2.Models;
using WebLynx2.Utilities;

namespace WebLynx2;

public sealed class ViewPropertyRow : INotifyPropertyChanged
{
    private static readonly string[] BooleanChoices = ["false", "true"];

    private string _key;
    private string _value;
    private ViewPropertyType _type;

    /// <summary>Key as it appeared when loaded from disk; empty for rows added in the UI.</summary>
    public string InitialKey { get; }

    /// <summary>Type as it appeared when loaded from disk.</summary>
    public ViewPropertyType InitialType { get; }

    /// <summary>Files that contained <see cref="InitialKey"/> at load time; empty for new rows (saved to shared <c>view.yaml</c> only).</summary>
    public IReadOnlyList<PropertySource> InitialSources { get; }

    public ViewPropertyRow(
        string initialKey,
        string initialValue,
        ViewPropertyType type,
        IReadOnlyList<PropertySource>? initialSources)
    {
        InitialKey = initialKey;
        InitialType = type;
        InitialSources = initialSources is { Count: > 0 }
            ? initialSources.ToArray()
            : Array.Empty<PropertySource>();
        _key = initialKey;
        _value = initialValue;
        _type = type;
    }

    public string SourcesSummary =>
        InitialSources.Count == 0
            ? "—"
            : string.Join(" · ", InitialSources.Select(s => s.DisplayName));

    public ViewPropertyType Type
    {
        get => _type;
        set
        {
            if (_type == value)
                return;
            _type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStringType));
            OnPropertyChanged(nameof(IsIntegerType));
            OnPropertyChanged(nameof(IsBooleanType));
            OnPropertyChanged(nameof(IsColorType));
        }
    }

    public bool IsStringType => Type == ViewPropertyType.String;
    public bool IsIntegerType => Type == ViewPropertyType.Integer;
    public bool IsBooleanType => Type == ViewPropertyType.Boolean;
    public bool IsColorType => Type == ViewPropertyType.Color;

    public IReadOnlyList<string> BooleanOptions => BooleanChoices;

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
            NotifyColorPresentationChanged();
        }
    }

    public decimal IntegerValue
    {
        get => int.TryParse(Value, out var parsed) ? parsed : 0m;
        set
        {
            var normalized = ((int)value).ToString();
            if (_value == normalized)
                return;
            _value = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IntegerValue));
        }
    }

    public IBrush ColorBackgroundBrush => CreateBrush(Value, fallback: "#cccccc");
    public IBrush ColorForegroundBrush => CreateBrush(ColorContrast.GetReadableTextColor(Value), fallback: "#000000");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyColorPresentationChanged()
    {
        OnPropertyChanged(nameof(ColorBackgroundBrush));
        OnPropertyChanged(nameof(ColorForegroundBrush));
    }

    private static IBrush CreateBrush(string? hex, string fallback)
    {
        if (Color.TryParse(hex, out var color))
            return new SolidColorBrush(color);

        return Color.TryParse(fallback, out var fallbackColor)
            ? new SolidColorBrush(fallbackColor)
            : Brushes.Gray;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
