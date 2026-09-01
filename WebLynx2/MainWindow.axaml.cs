using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.Logging;
using WebLynx2.Api;
using WebLynx2.Models;
using WebLynx2.Utilities;
using WebLynx2.UnofficialResults;
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
    private const string AllViewsFilterItem = "All";

    private bool _portFieldSync;
    private bool _pollingIntervalSync;
    private bool _delayedDisplaySync;
    private FinishLynxTcpServer? _tcpServer;
    private RaceHttpServer? _httpServer;
    private UnofficialResultsPoller? _unofficialResultsPoller;
    private RaceStateManager? _raceStateManager;
    private ILoggerFactory? _raceLogFactory;
    private readonly DispatcherTimer _raceStateRefreshTimer;

    private readonly KeyValueStoreService _keyValueStore = new();
    private readonly AnnouncementOverrideService _announcementOverride = new();

    private string? _viewsRootPath;
    private Dictionary<string, List<string>> _propertyLoadSnapshot = new(StringComparer.Ordinal);
    private readonly List<ViewPropertyRow> _allViewPropertyRows = new();

    public ObservableCollection<string> LoadedViews { get; } = new();

    public ObservableCollection<ViewPropertyRow> ViewProperties { get; } = new();

    public ObservableCollection<string> NetworkAddresses { get; } = new();

    public ObservableCollection<RaceRacerDisplayRow> RaceRacers { get; } = new();

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

        EventTitleTextBox.TextChanged += EventIdentity_TextChanged;
        EventSubtitleTextBox.TextChanged += EventIdentity_TextChanged;

        ResultsPollingIntervalNumericUpDown.Loaded += (_, _) => AttachPollingIntervalInnerTextBox();
        DelayedDisplaySecondsNumericUpDown.Loaded += (_, _) => AttachDelayedDisplayInnerTextBox();

        _raceStateRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _raceStateRefreshTimer.Tick += (_, _) => RefreshRaceStateDisplay();
        _raceStateRefreshTimer.Start();

        ClearRaceStateDisplay();
        UpdateForcedAnnouncementStatus();

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
        DelayedDisplaySecondsNumericUpDown.Value = Math.Clamp(ev.DelayedDisplaySeconds, 0, 60);

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

        var previousSelection = LoadedViewsListBox.SelectedItem as string;

        LoadedViews.Clear();
        LoadedViews.Add(AllViewsFilterItem);
        foreach (var v in discovery.DiscoveredViews
                     .Where(x => x.IsValid)
                     .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            LoadedViews.Add(v.Name);

        _propertyLoadSnapshot = discovery.LastPropertyCatalog.ToDictionary(
            e => e.Key,
            e => e.Sources
                .Select(s => s.PropertiesFilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.Ordinal);

        ApplyViewPropertiesFromCatalog(discovery.LastPropertyCatalog);

        if (previousSelection is not null && LoadedViews.Contains(previousSelection))
            LoadedViewsListBox.SelectedItem = previousSelection;
        else
            LoadedViewsListBox.SelectedItem = AllViewsFilterItem;

        ApplyViewPropertyFilter();
        SyncEventIdentityToKeyValueStore();
    }

    private void EventIdentity_TextChanged(object? sender, TextChangedEventArgs e) =>
        SyncEventIdentityToKeyValueStore();

    /// <summary>
    /// Publishes Event Title / Subtitle into the key-value store so race-data
    /// <c>keyValues</c> / <c>viewConfig</c> include <c>meetTitle</c> and <c>eventSubtitle</c>.
    /// </summary>
    private void SyncEventIdentityToKeyValueStore() =>
        EventIdentityConfig.ApplyTo(_keyValueStore, EventTitleTextBox.Text, EventSubtitleTextBox.Text);

    private void ApplyViewPropertiesFromCatalog(IReadOnlyList<DiscoveredViewProperty> catalog)
    {
        _allViewPropertyRows.Clear();
        foreach (var entry in catalog.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            _allViewPropertyRows.Add(new ViewPropertyRow(entry.Key, entry.Value, entry.Type, entry.Sources));
    }

    private void LoadedViewsListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        ApplyViewPropertyFilter();

    private void ApplyViewPropertyFilter()
    {
        var selected = LoadedViewsListBox.SelectedItem as string;
        IEnumerable<ViewPropertyRow> rows = _allViewPropertyRows;

        if (!string.IsNullOrEmpty(selected) &&
            !string.Equals(selected, AllViewsFilterItem, StringComparison.Ordinal))
        {
            rows = _allViewPropertyRows.Where(r => PropertyBelongsToView(r, selected));
        }

        ViewProperties.Clear();
        foreach (var row in rows)
            ViewProperties.Add(row);
    }

    private bool PropertyBelongsToView(ViewPropertyRow row, string viewName)
    {
        if (string.IsNullOrEmpty(_viewsRootPath))
            return false;

        var viewPropertiesPath = Path.GetFullPath(Path.Combine(_viewsRootPath, viewName, ViewPropertiesFiles.FileName));
        return row.InitialSources.Any(s =>
            string.Equals(s.PropertiesFilePath, viewPropertiesPath, StringComparison.OrdinalIgnoreCase));
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
            AppConfiguration.Save(BuildAppSettingsFromUi());

            IReadOnlyDictionary<string, IReadOnlyList<string>> snapshot = _propertyLoadSnapshot.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value,
                StringComparer.Ordinal);

            ViewPropertiesSaveService.Save(_viewsRootPath, _allViewPropertyRows, snapshot);

            _keyValueStore.Clear();
            foreach (var row in _allViewPropertyRows)
            {
                if (string.IsNullOrWhiteSpace(row.Key))
                    continue;
                _keyValueStore.SetValue(row.Key.Trim(), row.Value);
            }

            SyncEventIdentityToKeyValueStore();
            RefreshDiscoveredViews(AppConfiguration.Load().Server);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Could not save changes: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds appsettings from the Event / Server UI so paths and other fields persist across restarts.
    /// </summary>
    private AppSettings BuildAppSettingsFromUi()
    {
        var existing = AppConfiguration.Load();

        return new AppSettings
        {
            Event = new EventSettings
            {
                Title = EventTitleTextBox.Text ?? "",
                Subtitle = EventSubtitleTextBox.Text ?? "",
                UnofficialResultsPath = (UnofficialResultsPathTextBox.Text ?? "").Trim(),
                OfficialResultsPath = (OfficialResultsPathTextBox.Text ?? "").Trim(),
                FileEncoding = (FileEncodingComboBox.SelectedItem as ComboBoxItem)?.Content as string
                    ?? FileEncodingComboBox.SelectedItem?.ToString()
                    ?? existing.Event.FileEncoding,
                PollingIntervalSeconds = GetResultsPollingIntervalSeconds(),
                DelayedDisplaySeconds = GetDelayedDisplaySeconds()
            },
            Server = new ServerSettings
            {
                ResultsPort = TryParsePort(ResultsPortTextBox.Text, out var resultsPort)
                    ? resultsPort
                    : existing.Server.ResultsPort,
                ClockPort = TryParsePort(ClockPortTextBox.Text, out var clockPort)
                    ? clockPort
                    : existing.Server.ClockPort,
                HttpPort = TryParsePort(HttpPortTextBox.Text, out var httpPort)
                    ? httpPort
                    : existing.Server.HttpPort,
                ViewsDirectory = existing.Server.ViewsDirectory
            }
        };
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

    private void AttachDelayedDisplayInnerTextBox()
    {
        var inner = DelayedDisplaySecondsNumericUpDown
            .GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault();
        if (inner is null)
            return;

        inner.TextInput += PortField_TextInput;
        inner.TextChanged += DelayedDisplay_TextChanged;
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

    private void DelayedDisplay_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_delayedDisplaySync || sender is not TextBox box)
            return;

        var before = box.Text ?? "";
        var normalized = NormalizeDigitsCapped(before, 60);
        if (normalized == before)
            return;

        var caret = box.CaretIndex;
        _delayedDisplaySync = true;
        try
        {
            box.Text = normalized;
            box.CaretIndex = caret <= normalized.Length ? caret : normalized.Length;
        }
        finally
        {
            _delayedDisplaySync = false;
        }
    }

    private async void UnofficialResultsPathBrowse_OnClick(object? sender, RoutedEventArgs e) =>
        await PickFolderIntoTextBoxAsync(UnofficialResultsPathTextBox, "Unofficial results folder");

    private async void OfficialResultsPathBrowse_OnClick(object? sender, RoutedEventArgs e) =>
        await PickFolderIntoTextBoxAsync(OfficialResultsPathTextBox, "Official results folder");

    private void ForcedAnnouncementApply_OnClick(object? sender, RoutedEventArgs e)
    {
        _announcementOverride.Apply(ForcedAnnouncementTextBox.Text);
        UpdateForcedAnnouncementStatus();
    }

    private void ForcedAnnouncementClear_OnClick(object? sender, RoutedEventArgs e)
    {
        _announcementOverride.Clear();
        ForcedAnnouncementTextBox.Text = string.Empty;
        UpdateForcedAnnouncementStatus();
    }

    private void UpdateForcedAnnouncementStatus()
    {
        ForcedAnnouncementStatusTextBlock.Text = _announcementOverride.IsActive
            ? "Override active"
            : "Using FinishLynx announcements";
    }

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
        _raceStateRefreshTimer.Stop();

        if (_tcpServer is not null)
        {
            _tcpServer.StopAsync().GetAwaiter().GetResult();
            _tcpServer = null;
        }

        if (_httpServer is not null)
        {
            _httpServer.StopAsync().GetAwaiter().GetResult();
            _httpServer = null;
        }

        if (_unofficialResultsPoller is not null)
        {
            _unofficialResultsPoller.StopAsync().GetAwaiter().GetResult();
            _unofficialResultsPoller = null;
        }

        DisposeRaceFeed();
    }

    private async void StartServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryParsePort(ResultsPortTextBox.Text, out var resultsPort) ||
            !TryParsePort(ClockPortTextBox.Text, out var clockPort) ||
            !TryParsePort(HttpPortTextBox.Text, out var httpPort))
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
        var raceState = RaceFeedComposition.CreateRaceStateManager(raceLogFactory);

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

        var unofficialCatalog = new UnofficialResultsCatalog(
            raceLogFactory.CreateLogger("UnofficialResultsCatalog"))
        {
            FileEncoding = ResolveFileEncoding()
        };
        var unofficialPoller = new UnofficialResultsPoller(
            unofficialCatalog,
            raceLogFactory.CreateLogger("UnofficialResultsPoller"));

        var httpServer = new RaceHttpServer(
            raceLogFactory.CreateLogger<RaceHttpServer>(),
            raceState,
            _keyValueStore,
            GetDelayedDisplaySeconds(),
            viewsRootPath: _viewsRootPath,
            unofficialResults: unofficialCatalog,
            announcementOverride: _announcementOverride);

        try
        {
            await httpServer.StartAsync(httpPort);
        }
        catch (Exception ex)
        {
            await server.StopAsync();
            raceLogFactory.Dispose();
            await ShowErrorDialogAsync($"Could not start HTTP server: {ex.Message}");
            return;
        }

        var unofficialPath = (UnofficialResultsPathTextBox.Text ?? "").Trim();
        if (!string.IsNullOrEmpty(unofficialPath))
        {
            unofficialPoller.Start(unofficialPath, TimeSpan.FromSeconds(GetResultsPollingIntervalSeconds()));
        }

        _raceStateManager = raceState;
        _raceLogFactory = raceLogFactory;
        _tcpServer = server;
        _httpServer = httpServer;
        _unofficialResultsPoller = unofficialPoller;
        SetServerChromeRunning(true);
        ApplyHttpStatus(listening: true);
    }

    private async void StopServerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_tcpServer is null)
            return;

        StopServerButton.IsEnabled = false;
        try
        {
            if (_unofficialResultsPoller is not null)
                await _unofficialResultsPoller.StopAsync();

            if (_httpServer is not null)
                await _httpServer.StopAsync();

            await _tcpServer.StopAsync();
        }
        finally
        {
            _unofficialResultsPoller = null;
            _httpServer = null;
            _tcpServer = null;
            SetServerChromeRunning(false);
            ApplyHttpStatus(listening: false);
            DisposeRaceFeed();
        }
    }

    private void DisposeRaceFeed()
    {
        _raceStateManager = null;
        _raceLogFactory?.Dispose();
        _raceLogFactory = null;
        ClearRaceStateDisplay();
    }

    private void RefreshRaceStateDisplay()
    {
        if (_raceStateManager is null)
        {
            ClearRaceStateDisplay();
            return;
        }

        var race = _raceStateManager.GetCurrentRaceState();
        var delaySeconds = GetDelayedDisplaySeconds();

        RaceStatusTextBlock.Text = race.Status.ToString();
        RaceClockTextBlock.Text = RaceTimeFormatter.Format(race.CurrentTime);
        RaceEventTextBlock.Text = FormatEventSummary(race.Event);
        RaceFeedStatusTextBlock.Text = "Receiving FinishLynx feed";
        RaceAnnouncementTextBlock.Text = string.IsNullOrWhiteSpace(race.AnnouncementMessage)
            ? "—"
            : race.AnnouncementMessage;

        // Update rows in place — Clear()+Add every 100ms was thrashing Avalonia bindings
        // while the HTTP server also served 100ms race-data polls (original WebLynx has no UI grid).
        var ordered = race.Racers.OrderBy(r => r.Lane).ToList();
        while (RaceRacers.Count > ordered.Count)
            RaceRacers.RemoveAt(RaceRacers.Count - 1);

        for (var i = 0; i < ordered.Count; i++)
        {
            var racer = ordered[i];
            var lane = racer.Lane.ToString(CultureInfo.InvariantCulture);
            var name = racer.Name;
            var affiliation = racer.Affiliation;
            var place = racer.Place.PlaceText;
            var lapsRemaining = FormatLapsRemaining(racer.LapsRemaining);
            var delayedLapsRemaining = FormatLapsRemaining(racer.GetDelayedLapsRemaining(delaySeconds));
            var split = RaceTimeFormatter.Format(racer.CumulativeSplitTime);
            var finalTime = RaceTimeFormatter.Format(racer.FinalTime);
            var finished = racer.HasFinished ? "Yes" : string.Empty;

            if (i < RaceRacers.Count)
            {
                RaceRacers[i].Apply(
                    lane, name, affiliation, place,
                    lapsRemaining, delayedLapsRemaining, split, finalTime, finished);
            }
            else
            {
                var row = new RaceRacerDisplayRow();
                row.Apply(
                    lane, name, affiliation, place,
                    lapsRemaining, delayedLapsRemaining, split, finalTime, finished);
                RaceRacers.Add(row);
            }
        }
    }

    private int GetDelayedDisplaySeconds()
    {
        var value = DelayedDisplaySecondsNumericUpDown.Value;
        if (value is null)
            return 5;

        return (int)Math.Clamp(value.Value, 0, 60);
    }

    private int GetResultsPollingIntervalSeconds()
    {
        var value = ResultsPollingIntervalNumericUpDown.Value;
        if (value is null)
            return 1;

        return (int)Math.Clamp(value.Value, 1, 3600);
    }

    private Encoding ResolveFileEncoding()
    {
        var name = (FileEncodingComboBox.SelectedItem as ComboBoxItem)?.Content as string
                   ?? FileEncodingComboBox.SelectedItem?.ToString()
                   ?? "ISO-8859-1";
        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return Encoding.Latin1;
        }
    }

    private void ClearRaceStateDisplay()
    {
        RaceStatusTextBlock.Text = "—";
        RaceClockTextBlock.Text = "—";
        RaceEventTextBlock.Text = "—";
        RaceFeedStatusTextBlock.Text = _tcpServer is null
            ? "Start the server to receive race data"
            : "Waiting for race data";
        RaceAnnouncementTextBlock.Text = "—";
        RaceRacers.Clear();
    }

    private static string FormatEventSummary(RaceEvent? ev)
    {
        if (ev is null)
            return "—";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ev.EventName))
            parts.Add(ev.EventName);
        if (!string.IsNullOrWhiteSpace(ev.EventNumber))
            parts.Add($"#{ev.EventNumber}");
        if (ev.HeatNumber > 0)
            parts.Add($"Heat {ev.HeatNumber.ToString(CultureInfo.InvariantCulture)}");
        if (ev.RoundNumber > 0)
            parts.Add($"Round {ev.RoundNumber.ToString(CultureInfo.InvariantCulture)}");

        return parts.Count > 0 ? string.Join(" · ", parts) : "—";
    }

    private static string FormatLapsRemaining(decimal lapsRemaining) =>
        lapsRemaining == 0 ? string.Empty : lapsRemaining.ToString("0.##", CultureInfo.InvariantCulture);

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

    private void ApplyHttpStatus(bool listening)
    {
        HttpStatusTextBlock.Text = listening ? "Listening" : "Not Listening";
        ApplyStatusBadgeBrush(HttpStatusBadge, listening ? "StatusListeningBrush" : "StatusNotListeningBrush");
    }

    private static void ApplyStatusBadgeBrush(Border border, string resourceKey)
    {
        if (border.TryFindResource(resourceKey, out var res) && res is IBrush brush)
            border.Background = brush;
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
