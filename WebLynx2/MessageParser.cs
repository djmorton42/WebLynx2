using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.Parsing;

namespace WebLynx2;

/// <summary>
/// Detects message types, buffers fragmented StartList payloads, and delegates field parsing to typed parsers.
/// </summary>
public class MessageParser
{
    private readonly ILogger<MessageParser> _logger;
    private readonly RunningTimeParser _runningTimeParser;
    private readonly StartListMessageParser _startListMessageParser;
    private readonly StartedMessageParser _startedMessageParser;
    private readonly ResultsMessageParser _resultsMessageParser;
    private readonly AnnouncementMessageParser _announcementMessageParser;

    private readonly Dictionary<string, StringBuilder> _messageBuffers = new();
    private readonly Dictionary<string, DateTime> _bufferTimestamps = new();
    private readonly TimeSpan _bufferTimeout = TimeSpan.FromSeconds(5);

    public MessageParser(
        ILogger<MessageParser> logger,
        RunningTimeParser runningTimeParser,
        StartListMessageParser startListMessageParser,
        StartedMessageParser startedMessageParser,
        ResultsMessageParser resultsMessageParser,
        AnnouncementMessageParser announcementMessageParser)
    {
        _logger = logger;
        _runningTimeParser = runningTimeParser;
        _startListMessageParser = startListMessageParser;
        _startedMessageParser = startedMessageParser;
        _resultsMessageParser = resultsMessageParser;
        _announcementMessageParser = announcementMessageParser;
    }

    public MessageType DetectMessageType(string text)
    {
        if (text.Contains("Running time:"))
            return MessageType.RunningTime;
        if (text.Contains("*** StartListHeader ***"))
            return MessageType.StartListHeader;
        if (text.Contains("*** StartedHeader ***"))
            return MessageType.StartedHeader;
        if (text.Contains("*** ResultsHeader"))
            return MessageType.ResultsHeader;
        if (text.Contains("Message Header") && text.Contains("Message Trailer"))
            return MessageType.Announcement;

        return MessageType.Unknown;
    }

    public (MessageType messageType, string completeText) ProcessMessage(string text, string clientInfo)
    {
        CleanupExpiredBuffers();

        if (IsStartListContinuation(text))
        {
            var bufferKey = GetBufferKey(clientInfo);

            if (_messageBuffers.ContainsKey(bufferKey))
            {
                _logger.LogDebug(
                    "Appending continuation to buffered StartList message for {ClientInfo} (length: {Length})",
                    clientInfo,
                    text.Length);

                _messageBuffers[bufferKey].Append(text);
                _bufferTimestamps[bufferKey] = DateTime.UtcNow;

                var completeText = _messageBuffers[bufferKey].ToString();

                if (IsCompleteStartListMessage(completeText))
                {
                    _logger.LogInformation(
                        "Completed buffered StartList message for {ClientInfo} (total length: {Length})",
                        clientInfo,
                        completeText.Length);

                    _messageBuffers.Remove(bufferKey);
                    _bufferTimestamps.Remove(bufferKey);
                    return (MessageType.StartListHeader, completeText);
                }

                return (MessageType.Unknown, string.Empty);
            }

            _logger.LogWarning("Received StartList continuation for {ClientInfo} but no buffer exists", clientInfo);
        }

        if (text.Contains("*** StartListHeader ***"))
        {
            if (!IsCompleteStartListMessage(text))
            {
                var bufferKey = GetBufferKey(clientInfo);
                _messageBuffers[bufferKey] = new StringBuilder(text);
                _bufferTimestamps[bufferKey] = DateTime.UtcNow;

                _logger.LogInformation(
                    "Buffering incomplete StartList message for {ClientInfo} (length: {Length})",
                    clientInfo,
                    text.Length);

                return (MessageType.Unknown, string.Empty);
            }

            return (MessageType.StartListHeader, text);
        }

        var messageType = DetectMessageType(text);
        return (messageType, text);
    }

    private static bool IsStartListContinuation(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (Regex.IsMatch(trimmedLine, @"^\d+\s+\d+\s+"))
                return true;

            if (trimmedLine.Contains("*** StartList/Started/ResultsTrailer ***"))
                return true;
        }

        return false;
    }

    private static bool IsCompleteStartListMessage(string text) =>
        text.Contains("*** StartListHeader ***") &&
        text.Contains("*** StartList/Started/ResultsTrailer ***");

    private static string GetBufferKey(string clientInfo) => clientInfo;

    private void CleanupExpiredBuffers()
    {
        var expiredKeys = _bufferTimestamps
            .Where(kvp => DateTime.UtcNow - kvp.Value > _bufferTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _logger.LogWarning("Cleaning up expired message buffer for {ClientInfo}", key);
            _messageBuffers.Remove(key);
            _bufferTimestamps.Remove(key);
        }
    }

    public Dictionary<string, (int bufferLength, DateTime lastUpdated)> GetBufferStatus() =>
        _messageBuffers.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value.Length, _bufferTimestamps[kvp.Key]));

    public TimeSpan? ParseRunningTime(string text) => _runningTimeParser.Parse(text);

    public RaceEvent? ParseStartListHeader(string text) => _startListMessageParser.ParseHeader(text);

    public List<Racer> ParseRacersFromStartList(string text) => _startListMessageParser.ParseRacers(text);

    public List<Racer> ParseRacersFromStarted(string text) => _startedMessageParser.ParseRacers(text);

    public List<Racer> ParseRacersFromResults(string text) => _resultsMessageParser.ParseRacers(text);

    public string? ParseAnnouncementMessage(string text) => _announcementMessageParser.Parse(text);

    public static bool IsLapCountOnlyUpdate(List<Racer> racers) =>
        LapCountOnlyUpdateDetector.IsLapCountOnlyUpdate(racers);
}
