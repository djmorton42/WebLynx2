namespace WebLynx2.Utilities;

public static class RaceTimeFormatter
{
    public static string Format(TimeSpan? time)
    {
        if (time is null)
            return string.Empty;

        var totalSeconds = time.Value.TotalSeconds;
        var minutes = (int)(totalSeconds / 60);
        var seconds = totalSeconds % 60;

        return minutes > 0
            ? $"{minutes}:{seconds:00.000}"
            : $"{seconds:0.000}";
    }
}
