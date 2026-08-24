using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.Utilities;
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
    private ILoggerFactory? _raceLogFactory;

    private readonly KeyValueStoreService _keyValueStore = new();

    private string? _viewsRootPath;
    private Dictionary<string, List<string>> _propertyLoadSnapshot = new(StringComparer.Ordinal);

    public ObservableCollection<string> LoadedViews { get; } = new();

    public ObservableCollection<ViewPropertyRow> ViewProperties { get; } = new();

    public ObservableCollection<string> NetworkAddresses { get; } = new();

    /// <summary>Configuration values merged from <c>view.properties</c> and the properties grid (after Save).</summary>
    public KeyValueStoreService KeyValueStore => _keyValueStore;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        AttachTcpPortField(ResultsPortTextBox);
        AttachTcpPortField(ClockPortTextBox);
        AttachTcpPortField(HttpPortTextBox);

        ApplyAppSettings(AppConfiguration.Load());
        RefreshNetworkAddresses();

        ResultsPollingIntervalNumericUpDown.Loaded += (_, _) => AttachPollingIntervalInnerTextBox();

        Closing += MainWindow_OnClosing;
    }

    private void RefreshNetworkAddresses()
    {
        NetworkAddresses.Clear();
        foreach (var entry in NetworkAddressHelper.GetLocalIPv4Addresses())
            NetworkAddresses.Add(entry);

        NoNetworkAddressesTextBlock.IsVisible = NetworkAddresses.Count == 0;
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

        RefreshDiscoveredViews(srv);
    }

    private void RefreshDiscoveredViews(ServerSettings server)
    {
        var root = ViewDiscoveryService.ResolveViewsRoot(server.ViewsDirectory);
        _viewsRootPath = root;

        var discovery = new ViewDiscoveryService(root, keyValueStore: _keyValueStore);
        discovery.DiscoverViews();

        LoadedViews.Clear();
        foreach (var v in discovery.DiscoveredViews.Where(x => x.IsValid))
            LoadedViews.Add(v.Name);

        _propertyLoadSnapshot = discovery.LastPropertyCatalog.ToDictionary(
            e => e.Key,
            e => e.Sources
                .Select(s => s.PropertiesFilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.Ordinal);

        ApplyViewPropertiesFromCatalog(discovery.LastPropertyCatalog);
    }

    private void ApplyViewPropertiesFromCatalog(IReadOnlyList<DiscoveredViewProperty> catalog)
    {
        ViewProperties.Clear();
        foreach (var entry in catalog.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            ViewProperties.Add(new ViewPropertyRow(entry.Key, entry.Value, entry.Sources));
    }

    private async void SaveChanges_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewsRootPath))
        {
            await ShowErrorDialogAsync("Views directory is not set.");
            return;
        }

        try
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> snapshot = _propertyLoadSnapshot.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value,
                StringComparer.Ordinal);

            ViewPropertiesSaveService.Save(_viewsRootPath, ViewProperties.ToList(), snapshot);

            _keyValueStore.Clear();
            foreach (var row in ViewProperties)
            {
                if (string.IsNullOrWhiteSpace(row.Key))
                    continue;
                _keyValueStore.SetValue(row.Key.Trim(), row.Value);
            }

            RefreshDiscoveredViews(AppConfiguration.Load().Server);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Could not save view properties: {ex.Message}");
        }
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

    private void ExitButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_tcpServer is not null)
        {
            _tcpServer.StopAsync().GetAwaiter().GetResult();
            _tcpServer = null;
        }

        DisposeRaceFeed();
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

        DisposeRaceFeed();

        var raceLogFactory = LoggerFactory.Create(b => b.AddDebug());
        var raceState = RaceFeedComposition.CreateRaceStateManager(raceLogFactory, new LapCounterSettings());

        var logger = new ReceivedDataFileLogger();
        var server = new FinishLynxTcpServer(logger, raceState, OnTcpChannelStatusFromBackground);
        try
        {
            server.Start(clockPort, resultsPort);
        }
        catch (Exception ex)
        {
            raceLogFactory.Dispose();
            await ShowErrorDialogAsync($"Could not start TCP servers: {ex.Message}");
            return;
        }

        _raceLogFactory = raceLogFactory;
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
            DisposeRaceFeed();
        }
    }

    private void DisposeRaceFeed()
    {
        _raceLogFactory?.Dispose();
        _raceLogFactory = null;
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
