using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace WebLynx2;

public partial class MainWindow : Window
{
    private bool _portFieldSync;
    private bool _pollingIntervalSync;

    public ObservableCollection<string> LoadedViews { get; } = new() { "Common" };

    public ObservableCollection<ViewPropertyRow> ViewProperties { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        AttachTcpPortField(ResultsPortTextBox);
        AttachTcpPortField(ClockPortTextBox);
        AttachTcpPortField(HttpPortTextBox);

        ResultsPollingIntervalNumericUpDown.Loaded += (_, _) => AttachPollingIntervalInnerTextBox();

        SyncRemoveViewPropertyButtonEnabled();
    }

    private void ViewPropertiesListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SyncRemoveViewPropertyButtonEnabled();

    private void SyncRemoveViewPropertyButtonEnabled() =>
        RemoveViewPropertyButton.IsEnabled = ViewPropertiesListBox.SelectedItem is ViewPropertyRow;

    private void ViewPropertyKeyTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        if (sender is not TextBox keyBox)
            return;

        if (keyBox.Parent is not Grid grid)
            return;

        TextBox? valueBox = null;
        foreach (var child in grid.Children)
        {
            if (child is TextBox tb && Grid.GetColumn(tb) == 1)
            {
                valueBox = tb;
                break;
            }
        }

        if (valueBox is null)
            return;

        e.Handled = true;
        valueBox.Focus();
    }

    private void AttachPollingIntervalInnerTextBox()
    {
        var inner = ResultsPollingIntervalNumericUpDown
            .GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault();
        if (inner is null)
            return;

        inner.TextInput += PortField_TextInput;
        inner.TextChanged += PollingInterval_TextChanged;
    }

    private void AttachTcpPortField(TextBox box)
    {
        box.TextInput += PortField_TextInput;
        box.TextChanged += PortField_TextChanged;
    }

    private static void PortField_TextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text is null || e.Text.Length == 0)
            return;

        if (!e.Text.All(c => c is >= '0' and <= '9'))
            e.Handled = true;
    }

    private void PortField_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_portFieldSync || sender is not TextBox box)
            return;

        var before = box.Text ?? "";
        var normalized = NormalizeDigitsCapped(before, 65535);
        if (normalized == before)
            return;

        var caret = box.CaretIndex;
        _portFieldSync = true;
        try
        {
            box.Text = normalized;
            box.CaretIndex = caret <= normalized.Length ? caret : normalized.Length;
        }
        finally
        {
            _portFieldSync = false;
        }
    }

    /// <summary>
    /// Keeps only ASCII digits and caps the numeric value at <paramref name="max"/>.
    /// </summary>
    private static string NormalizeDigitsCapped(string raw, int max)
    {
        var digits = new string((raw ?? "").Where(c => c is >= '0' and <= '9').ToArray());
        if (digits.Length == 0)
            return "";

        var v = 0;
        foreach (var c in digits)
        {
            v = v * 10 + (c - '0');
            if (v > max)
                return max.ToString();
        }

        return digits;
    }

    private void PollingInterval_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_pollingIntervalSync || sender is not TextBox box)
            return;

        var before = box.Text ?? "";
        var normalized = NormalizeDigitsCapped(before, 3600);
        if (normalized == before)
            return;

        var caret = box.CaretIndex;
        _pollingIntervalSync = true;
        try
        {
            box.Text = normalized;
            box.CaretIndex = caret <= normalized.Length ? caret : normalized.Length;
        }
        finally
        {
            _pollingIntervalSync = false;
        }
    }

    private async void UnofficialResultsPathBrowse_OnClick(object? sender, RoutedEventArgs e) =>
        await PickFolderIntoTextBoxAsync(UnofficialResultsPathTextBox, "Unofficial results folder");

    private async void OfficialResultsPathBrowse_OnClick(object? sender, RoutedEventArgs e) =>
        await PickFolderIntoTextBoxAsync(OfficialResultsPathTextBox, "Official results folder");

    private async Task PickFolderIntoTextBoxAsync(TextBox target, string dialogTitle)
    {
        var top = TopLevel.GetTopLevel(this);
        var storage = top?.StorageProvider;
        if (storage is null || !storage.CanPickFolder)
            return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = dialogTitle,
            AllowMultiple = false
        });

        if (folders.Count < 1)
            return;

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            target.Text = path;
    }

    private void AddViewProperty_OnClick(object? sender, RoutedEventArgs e) =>
        ViewProperties.Add(new ViewPropertyRow());

    private void RemoveSelectedViewProperty_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewPropertiesListBox.SelectedItem is not ViewPropertyRow row)
            return;

        ViewProperties.Remove(row);
        SyncRemoveViewPropertyButtonEnabled();
    }

    private void ExitButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
