namespace WebLynx2.Utilities;

public static class TimeSpanParser
{
    public static TimeSpan? Parse(string timeString)
    {
        if (string.IsNullOrWhiteSpace(timeString) || timeString == "0.00" || timeString == "0")
            return null;

        try
        {
            if (timeString.Contains(':'))
            {
                var parts = timeString.Split(':');
                var minutes = int.Parse(parts[0]);
                var seconds = double.Parse(parts[1]);
                return TimeSpan.FromMinutes(minutes).Add(TimeSpan.FromSeconds(seconds));
            }

            var sec = double.Parse(timeString);
            return TimeSpan.FromSeconds(sec);
        }
        catch
        {
            return null;
        }
    }
}
