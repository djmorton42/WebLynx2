using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WebLynx2;

public sealed class RaceRacerDisplayRow : INotifyPropertyChanged
{
    private string _lane = string.Empty;
    private string _name = string.Empty;
    private string _affiliation = string.Empty;
    private string _place = string.Empty;
    private string _lapsRemaining = string.Empty;
    private string _delayedLapsRemaining = string.Empty;
    private string _split = string.Empty;
    private string _finalTime = string.Empty;
    private string _finished = string.Empty;

    public string Lane
    {
        get => _lane;
        set => SetField(ref _lane, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Affiliation
    {
        get => _affiliation;
        set => SetField(ref _affiliation, value);
    }

    public string Place
    {
        get => _place;
        set => SetField(ref _place, value);
    }

    public string LapsRemaining
    {
        get => _lapsRemaining;
        set => SetField(ref _lapsRemaining, value);
    }

    public string DelayedLapsRemaining
    {
        get => _delayedLapsRemaining;
        set => SetField(ref _delayedLapsRemaining, value);
    }

    public string Split
    {
        get => _split;
        set => SetField(ref _split, value);
    }

    public string FinalTime
    {
        get => _finalTime;
        set => SetField(ref _finalTime, value);
    }

    public string Finished
    {
        get => _finished;
        set => SetField(ref _finished, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(
        string lane,
        string name,
        string affiliation,
        string place,
        string lapsRemaining,
        string delayedLapsRemaining,
        string split,
        string finalTime,
        string finished)
    {
        Lane = lane;
        Name = name;
        Affiliation = affiliation;
        Place = place;
        LapsRemaining = lapsRemaining;
        DelayedLapsRemaining = delayedLapsRemaining;
        Split = split;
        FinalTime = finalTime;
        Finished = finished;
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
