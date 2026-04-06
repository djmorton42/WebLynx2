using System.Text;
using Microsoft.Extensions.Logging;
using WebLynx2.Models;

namespace WebLynx2;

public class RaceStateManager
{
    private readonly ILogger<RaceStateManager> _logger;
    private readonly MessageParser _messageParser;
    private readonly LapCounterSettings _lapCounterSettings;

    public RaceData CurrentRace { get; private set; } = new();

    public event EventHandler<RaceData>? RaceUpdated;

    public RaceStateManager(
        ILogger<RaceStateManager> logger,
        MessageParser messageParser,
        LapCounterSettings lapCounterSettings)
    {
        _logger = logger;
        _messageParser = messageParser;
        _lapCounterSettings = lapCounterSettings;
    }

    public void ProcessMessage(byte[] data, string clientInfo)
    {
        try
        {
            var text = DecodeMessage(data);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Received empty or invalid message from {ClientInfo}", clientInfo);
                return;
            }

            var (messageType, completeText) = _messageParser.ProcessMessage(text, clientInfo);

            if (messageType == MessageType.Unknown && string.IsNullOrEmpty(completeText))
            {
                _logger.LogDebug("Message buffered for {ClientInfo}, waiting for completion", clientInfo);
                return;
            }

            if (messageType != MessageType.RunningTime)
                _logger.LogInformation("Processing {MessageType} message from {ClientInfo}", messageType, clientInfo);

            switch (messageType)
            {
                case MessageType.RunningTime:
                    ProcessRunningTimeMessage(completeText);
                    break;
                case MessageType.StartListHeader:
                    ProcessStartListHeaderMessage(completeText);
                    break;
                case MessageType.StartedHeader:
                    ProcessStartedHeaderMessage(completeText);
                    break;
                case MessageType.ResultsHeader:
                    ProcessResultsHeaderMessage(completeText);
                    break;
                case MessageType.Announcement:
                    ProcessAnnouncementMessage(completeText);
                    break;
                default:
                    _logger.LogWarning("Unknown message type from {ClientInfo}: {Text}", clientInfo, completeText);
                    break;
            }

            CurrentRace.LastUpdated = DateTime.UtcNow;
            _logger.LogDebug("Notifying subscribers of race update");
            RaceUpdated?.Invoke(this, CurrentRace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {ClientInfo}", clientInfo);
        }
    }

    private void ProcessRunningTimeMessage(string text)
    {
        var runningTime = _messageParser.ParseRunningTime(text);
        if (runningTime is null)
            return;

        CurrentRace.CurrentTime = runningTime.Value;

        if (CurrentRace.Status == RaceStatus.NotStarted)
        {
            CurrentRace.Status = RaceStatus.Running;
            _logger.LogInformation("Race started - status changed to Running (triggered by running time)");
            HandleHalfLapModeRaceStart();
        }
    }

    private void ProcessStartListHeaderMessage(string text)
    {
        _logger.LogInformation("StartListHeader received - clearing existing race state and loading new race");

        CurrentRace = new RaceData();

        var eventData = _messageParser.ParseStartListHeader(text);
        if (eventData is not null)
        {
            CurrentRace.Event = eventData;
            _logger.LogInformation("Loaded new event data: {EventName}", eventData.EventName);
        }

        var racers = _messageParser.ParseRacersFromStartList(text);
        _logger.LogDebug("Parsed {Count} racers from StartList text", racers.Count);

        CurrentRace.Racers = racers;

        foreach (var racer in racers)
            racer.InitializeDelayedLapCount();

        if (racers.Count > 0)
            _logger.LogInformation("Loaded new racer list with {Count} racers", racers.Count);
        else
            _logger.LogWarning("No racers parsed from StartList message");

        CurrentRace.LastUpdated = DateTime.UtcNow;
    }

    private void ProcessStartedHeaderMessage(string text)
    {
        var wasNotStarted = CurrentRace.Status == RaceStatus.NotStarted;

        CurrentRace.Status = RaceStatus.Running;
        _logger.LogInformation("Race started - status changed to Running (triggered by StartedHeader)");

        if (wasNotStarted)
            HandleHalfLapModeRaceStart();

        var eventData = _messageParser.ParseStartListHeader(text);
        if (eventData is not null)
            CurrentRace.Event = eventData;

        var racers = _messageParser.ParseRacersFromStarted(text);
        if (racers.Count == 0)
            return;

        var isLapCountOnlyUpdate = MessageParser.IsLapCountOnlyUpdate(racers);

        foreach (var racer in racers)
        {
            var existingRacer = CurrentRace.Racers.FirstOrDefault(r => r.Lane == racer.Lane);
            if (existingRacer is not null)
            {
                existingRacer.Place = racer.Place;
                existingRacer.ReactionTime = racer.ReactionTime;
                existingRacer.CumulativeSplitTime = racer.CumulativeSplitTime;
                existingRacer.LastSplitTime = racer.LastSplitTime;
                existingRacer.BestSplitTime = racer.BestSplitTime;
                existingRacer.UpdateLapsRemaining(racer.LapsRemaining, skipDelay: isLapCountOnlyUpdate);
                existingRacer.Speed = racer.Speed;
                existingRacer.Pace = racer.Pace;
                HandleHalfLapModeFirstCrossing(existingRacer);
            }
            else
            {
                racer.InitializeDelayedLapCount();
                CurrentRace.Racers.Add(racer);
                HandleHalfLapModeFirstCrossing(racer);
            }
        }

        _logger.LogInformation("Updated racer progress data for {Count} racers", racers.Count);
    }

    private void ProcessResultsHeaderMessage(string text)
    {
        _logger.LogInformation("Processing ResultsHeader message");

        var eventData = _messageParser.ParseStartListHeader(text);
        if (eventData is not null)
            CurrentRace.Event = eventData;

        var racers = _messageParser.ParseRacersFromResults(text);
        _logger.LogInformation("Parsed {Count} racers from Results message", racers.Count);

        if (racers.Count == 0)
            return;

        foreach (var racer in racers)
        {
            var existingRacer = CurrentRace.Racers.FirstOrDefault(r => r.Lane == racer.Lane);
            if (existingRacer is not null)
            {
                _logger.LogInformation("Updating racer {Lane} with final time {FinalTime}", racer.Lane, racer.FinalTime);
                existingRacer.Place = racer.Place;
                existingRacer.FinalTime = racer.FinalTime;
                existingRacer.DeltaTime = racer.DeltaTime;
                existingRacer.ReactionTime = racer.ReactionTime;
                existingRacer.HasFinished = racer.HasFinished;
            }
            else
            {
                CurrentRace.Racers.Add(racer);
            }
        }

        CurrentRace.Status = RaceStatus.Finished;
        _logger.LogInformation("Updated final results for {Count} racers", racers.Count);
    }

    private void ProcessAnnouncementMessage(string text)
    {
        _logger.LogInformation("Processing Announcement message");

        var announcementMessage = _messageParser.ParseAnnouncementMessage(text);
        CurrentRace.AnnouncementMessage = announcementMessage;

        if (!string.IsNullOrEmpty(announcementMessage))
            _logger.LogInformation("Updated announcement message: {Message}", announcementMessage);
        else
            _logger.LogInformation("Cleared announcement message");
    }

    private string DecodeMessage(byte[] data)
    {
        try
        {
            if (data.Length % 2 == 0)
            {
                var utf16Text = Encoding.Unicode.GetString(data);
                var utf16PrintableCount = utf16Text.Count(c =>
                    char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsWhiteSpace(c));
                var utf16PrintableRatio = (double)utf16PrintableCount / utf16Text.Length;

                if (utf16PrintableRatio > 0.7)
                    return utf16Text;
            }

            var utf8PrintableCount = data.Count(b => b is >= 32 and <= 126);
            var utf8PrintableRatio = (double)utf8PrintableCount / data.Length;

            if (utf8PrintableRatio > 0.8)
                return Encoding.UTF8.GetString(data);

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decoding message data");
            return string.Empty;
        }
    }

    public void ResetRace()
    {
        CurrentRace = new RaceData();
        _logger.LogInformation("Race state manually reset - all data cleared");
    }

    public RaceData GetCurrentRaceState() => CurrentRace;

    private bool HasHalfLapLaps() => CurrentRace.Racers.Any(r => r.LapsRemaining % 1 == 0.5m);

    private void HandleHalfLapModeRaceStart()
    {
        if (!_lapCounterSettings.HalfLapModeEnabled)
            return;

        if (HasHalfLapLaps())
        {
            _logger.LogInformation("Half-lap mode: Half-lap race detected, timing software handles lap counts");
            foreach (var racer in CurrentRace.Racers)
                racer.LapCountLastChanged = DateTime.UtcNow;
        }
        else
        {
            _logger.LogInformation("Half-lap mode: Adding 1 to delayedLapsRemaining for whole-lap race at race start");
            foreach (var racer in CurrentRace.Racers)
            {
                var currentLaps = racer.LapsRemaining;
                racer.DelayedLapsRemaining = currentLaps + 1;
                racer.LapCountLastChanged = DateTime.UtcNow;
            }
        }
    }

    private void HandleHalfLapModeFirstCrossing(Racer racer)
    {
        if (!_lapCounterSettings.HalfLapModeEnabled)
            return;

        if (!HasHalfLapLaps())
            return;

        _logger.LogDebug("Half-lap mode: Timing software handles lap count changes for half-lap races");
    }
}
