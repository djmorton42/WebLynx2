using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WebLynx2;

public sealed class ViewPropertyRow : INotifyPropertyChanged
{
    private string _key = "";
    private string _value = "";

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
