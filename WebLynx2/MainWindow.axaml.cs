using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WebLynx2;

public partial class MainWindow : Window
{
    private bool _portFieldSync;
    private bool _pollingIntervalSync;
    private FinishLynxTcpServer? _tcpServer;

    public ObservableCollection<string> LoadedViews { get; } = new() { "Common" };

    public ObservableCollection<ViewPropertyRow> ViewProperties { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        AttachTcpPortField(ResultsPortTextBox);
        AttachTcpPortField(ClockPortTextBox);
        AttachTcpPortField(HttpPortTextBox);

        ApplyAppSettings(AppConfiguration.Load());

        ResultsPollingIntervalNumericUpDown.Loaded += (_, _) => AttachPollingIntervalInnerTextBox();

        SyncRemoveViewPropertyButtonEnabled();

        Closing += MainWindow_OnClosing;
    }

    private void ApplyAppSettings(AppSettings settings)
    {
        var ev = settings.Event;
        EventTitleTextBox.Text = ev.Title;
        EventSubtitleTextBox.Text = ev.Subtitle;
        UnofficialResultsPathTextBox.Text = ev.UnofficialResultsPath;
        OfficialResultsPathTextBox.Text = ev.OfficialResultsPath;
        SelectFileEncodingComboItem(FileEncodingComboBox, ev.FileEncoding);
        ResultsPollingIntervalNumericUpDown.Value = Math.Clamp(ev.PollingIntervalSeconds, 1, 3600);

        var srv = settings.Server;
        ResultsPortTextBox.Text = srv.ResultsPort.ToString();
        ClockPortTextBox.Text = srv.ClockPort.ToString();
        HttpPortTextBox.Text = srv.HttpPort.ToString();
    }

    private static void SelectFileEncodingComboItem(ComboBox combo, string encodingName)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem { Content: string s } && string.Equals(s, encodingName, StringComparison.Ordinal))
            {
                combo.SelectedItem = item;
                return;
            }
        }
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

    private void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_tcpServer is null)
            return;

        _tcpServer.StopAsync().GetAwaiter().GetResult();
        _tcpServer = null;
    }

    private async void StartServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryParsePort(ResultsPortTextBox.Text, out var resultsPort) ||
            !TryParsePort(ClockPortTextBox.Text, out var clockPort) ||
            !TryParsePort(HttpPortTextBox.Text, out _))
        {
            await ShowErrorDialogAsync("Enter valid port numbers from 1 to 65535 for all three fields.");
            return;
        }

        if (resultsPort == clockPort)
        {
            await ShowErrorDialogAsync("FinishLynx results and clock ports must be different.");
            return;
        }

        var logger = new ReceivedDataFileLogger();
        var server = new FinishLynxTcpServer(logger, OnTcpChannelStatusFromBackground);
        try
        {
            server.Start(clockPort, resultsPort);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Could not start TCP servers: {ex.Message}");
            return;
        }

        _tcpServer = server;
        SetServerChromeRunning(true);
    }

    private async void StopServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_tcpServer is null)
            return;

        StopServerButton.IsEnabled = false;
        try
        {
            await _tcpServer.StopAsync();
        }
        finally
        {
            _tcpServer = null;
            SetServerChromeRunning(false);
        }
    }

    private void OnTcpChannelStatusFromBackground(TcpChannelKind kind, TcpChannelUiStatus status)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ApplyTcpChannelStatus(kind, status);
        else
            Dispatcher.UIThread.Post(() => ApplyTcpChannelStatus(kind, status));
    }

    private void ApplyTcpChannelStatus(TcpChannelKind kind, TcpChannelUiStatus status)
    {
        var border = kind == TcpChannelKind.Clock ? ClockStatusBadge : ResultsStatusBadge;
        var text = kind == TcpChannelKind.Clock ? ClockStatusTextBlock : ResultsStatusTextBlock;

        text.Text = status switch
        {
            TcpChannelUiStatus.NotListening => "Not Listening",
            TcpChannelUiStatus.Listening => "Listening",
            TcpChannelUiStatus.Connected => "Connected",
            _ => text.Text
        };

        var key = status switch
        {
            TcpChannelUiStatus.NotListening => "StatusNotListeningBrush",
            TcpChannelUiStatus.Listening => "StatusListeningBrush",
            TcpChannelUiStatus.Connected => "StatusConnectedBrush",
            _ => "StatusNotListeningBrush"
        };

        if (this.TryFindResource(key, out var res) && res is IBrush brush)
            border.Background = brush;
    }

    private void SetServerChromeRunning(bool running)
    {
        StartServerButton.IsEnabled = !running;
        StopServerButton.IsEnabled = running;
        ResultsPortTextBox.IsEnabled = !running;
        ClockPortTextBox.IsEnabled = !running;
        HttpPortTextBox.IsEnabled = !running;
    }

    private static bool TryParsePort(string? text, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port)
               && port is >= 1 and <= 65535;
    }

    private async Task ShowErrorDialogAsync(string message)
    {
        var ok = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 80
        };

        var dialog = new Window
        {
            Title = "WebLynx2",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 400
                    },
                    ok
                }
            }
        };

        ok.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }
}
