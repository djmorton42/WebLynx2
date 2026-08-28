using WebLynx2;
using Xunit;

namespace WebLynx2.Tests.Unit;

public class AppConfigurationTests
{
    [Fact]
    public void SaveTo_ThenLoadFrom_RoundTripsResultsPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "WebLynx2AppSettings_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings
            {
                Event =
                {
                    Title = "Meet",
                    Subtitle = "Day 1",
                    UnofficialResultsPath = "/tmp/unofficial",
                    OfficialResultsPath = "/tmp/official",
                    FileEncoding = "UTF-8",
                    PollingIntervalSeconds = 2,
                    DelayedDisplaySeconds = 3
                },
                Server =
                {
                    ResultsPort = 9001,
                    ClockPort = 9000,
                    HttpPort = 5002,
                    ViewsDirectory = "/tmp/views"
                }
            };

            AppConfiguration.SaveTo(path, settings);
            var loaded = AppConfiguration.LoadFrom(path);

            Assert.Equal("/tmp/unofficial", loaded.Event.UnofficialResultsPath);
            Assert.Equal("/tmp/official", loaded.Event.OfficialResultsPath);
            Assert.Equal("Meet", loaded.Event.Title);
            Assert.Equal("Day 1", loaded.Event.Subtitle);
            Assert.Equal("UTF-8", loaded.Event.FileEncoding);
            Assert.Equal(2, loaded.Event.PollingIntervalSeconds);
            Assert.Equal(3, loaded.Event.DelayedDisplaySeconds);
            Assert.Equal(9001, loaded.Server.ResultsPort);
            Assert.Equal(9000, loaded.Server.ClockPort);
            Assert.Equal(5002, loaded.Server.HttpPort);
            Assert.Equal("/tmp/views", loaded.Server.ViewsDirectory);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void LoadFrom_MissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "WebLynx2Missing_" + Guid.NewGuid().ToString("N") + ".json");

        var loaded = AppConfiguration.LoadFrom(path);

        Assert.Equal(".", loaded.Event.UnofficialResultsPath);
        Assert.Equal(".", loaded.Event.OfficialResultsPath);
        Assert.Equal(8081, loaded.Server.ResultsPort);
    }
}
