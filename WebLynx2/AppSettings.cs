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
}

public sealed class ServerSettings
{
    public int ResultsPort { get; set; } = 8081;

    public int ClockPort { get; set; } = 8080;

    public int HttpPort { get; set; } = 5001;
}
