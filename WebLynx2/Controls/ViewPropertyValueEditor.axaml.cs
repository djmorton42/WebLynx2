using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace WebLynx2.Controls;

public partial class ViewPropertyValueEditor : UserControl
{
    public ViewPropertyValueEditor()
    {
        InitializeComponent();
    }

    private void PickColor_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewPropertyRow row)
            return;

        var currentColor = Color.TryParse(row.Value, out var parsed) ? parsed : Colors.Gray;

        var picker = new ColorPicker
        {
            Color = currentColor
        };

        var flyout = new Flyout
        {
            Content = picker
        };

        EventHandler<ColorChangedEventArgs>? onColorChanged = (_, args) =>
        {
            var c = args.NewColor;
            row.Value = $"#{c.R:X2}{c.G:X2}{c.B:X2}".ToLowerInvariant();
        };

        picker.ColorChanged += onColorChanged;
        flyout.Closed += (_, _) => picker.ColorChanged -= onColorChanged;

        if (sender is Control anchor)
            flyout.ShowAt(anchor);
    }
}
