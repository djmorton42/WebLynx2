using Microsoft.Extensions.Logging;
using WebLynx2.Models;
using WebLynx2.Parsing;

namespace WebLynx2;

/// <summary>
/// Wires <see cref="MessageParser"/> with per-message-type parsers matching the WebLynx desktop service layout.
/// </summary>
public static class RaceFeedComposition
{
    public static RaceStateManager CreateRaceStateManager(ILoggerFactory loggerFactory, LapCounterSettings lapCounterSettings)
    {
        var lapCount = new LapCountParser(loggerFactory.CreateLogger<LapCountParser>());
        var running = new RunningTimeParser(loggerFactory.CreateLogger<RunningTimeParser>());
        var startList = new StartListMessageParser(loggerFactory.CreateLogger<StartListMessageParser>(), lapCount);
        var started = new StartedMessageParser(loggerFactory.CreateLogger<StartedMessageParser>(), lapCount);
        var results = new ResultsMessageParser(loggerFactory.CreateLogger<ResultsMessageParser>());
        var announcement = new AnnouncementMessageParser(loggerFactory.CreateLogger<AnnouncementMessageParser>());

        var messageParser = new MessageParser(
            loggerFactory.CreateLogger<MessageParser>(),
            running,
            startList,
            started,
            results,
            announcement);

        return new RaceStateManager(
            loggerFactory.CreateLogger<RaceStateManager>(),
            messageParser,
            lapCounterSettings);
    }
}
