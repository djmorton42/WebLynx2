namespace WebLynx2;

public sealed class AppSettings
{
    public EventSettings Event { get; set; } = new();

    public ServerSettings Server { get; set; } = new();
}

public sealed class EventSettings
{
    public string Title { get; set; } = "";

    public string Subtitle { get; set; } = "";

    public string UnofficialResultsPath { get; set; } = ".";

    public string OfficialResultsPath { get; set; } = ".";

    public string FileEncoding { get; set; } = "ISO-8859-1";

    public int PollingIntervalSeconds { get; set; } = 1;

    /// <summary>
    /// Seconds to keep showing the previous lap count after it changes (delayed lap display).
    /// </summary>
    public int DelayedDisplaySeconds { get; set; } = 5;
}

public sealed class ServerSettings
{
    public int ResultsPort { get; set; } = 8081;

    public int ClockPort { get; set; } = 8080;

    public int HttpPort { get; set; } = 5001;

    /// <summary>
    /// Directory containing one subdirectory per view (same layout as WebLynx). Relative paths are resolved under the application base directory.
    /// </summary>
    public string ViewsDirectory { get; set; } = "Views";
}
