using System.Text;
using WebLynx2.Tests.Unit;
using WebLynx2.UnofficialResults;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class UnofficialResultsPollerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WebLynx2Poller_" + Guid.NewGuid().ToString("N"));

    public UnofficialResultsPollerTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Start_PollsDirectoryUntilStopped()
    {
        var catalog = new UnofficialResultsCatalog { FileEncoding = Encoding.Latin1 };
        await using var poller = new UnofficialResultsPoller(catalog);

        poller.Start(_dir, TimeSpan.FromMilliseconds(50));
        Assert.True(poller.IsRunning);

        await File.WriteAllTextAsync(
            Path.Combine(_dir, "08A.lif"),
            LifFileParserTests.CompleteRaceLif,
            Encoding.Latin1);

        await WaitForAsync(() => catalog.Count == 1, TimeSpan.FromSeconds(3));

        Assert.Equal("8A", catalog.GetLatestRace()!.RaceNumber);

        await poller.StopAsync();
        Assert.False(poller.IsRunning);
    }

    [Fact]
    public void Start_EmptyPath_DoesNotRun()
    {
        var catalog = new UnofficialResultsCatalog();
        var poller = new UnofficialResultsPoller(catalog);

        poller.Start("  ", TimeSpan.FromSeconds(1));

        Assert.False(poller.IsRunning);
    }

    [Fact]
    public async Task StartTwice_Throws()
    {
        var catalog = new UnofficialResultsCatalog();
        await using var poller = new UnofficialResultsPoller(catalog);
        poller.Start(_dir, TimeSpan.FromSeconds(30));

        Assert.Throws<InvalidOperationException>(() => poller.Start(_dir, TimeSpan.FromSeconds(30)));
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (condition())
                return;
            await Task.Delay(25);
        }

        Assert.True(condition(), "Condition was not met before timeout.");
    }
}
