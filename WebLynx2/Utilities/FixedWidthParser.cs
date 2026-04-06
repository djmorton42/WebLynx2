namespace WebLynx2.Utilities;

public static class FixedWidthParser
{
    public static T? Parse<T>(string text, int startIndex, int length, Func<string, T> func, T? defaultValue = default)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than zero.");
        if (startIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Start index cannot be negative.");

        if (text.Length >= startIndex + 1)
        {
            var substring = text.Substring(startIndex, Math.Min(length, text.Length - startIndex));
            if (string.IsNullOrWhiteSpace(substring))
                return defaultValue;

            try
            {
                return func.Invoke(substring);
            }
            catch
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }

    public static string? Parse(string text, int startIndex, int length, string defaultValue = "") =>
        Parse(text, startIndex, length, s => s, defaultValue);

    public static T? TrimParse<T>(string text, int startIndex, int length, Func<string, T> func, T? defaultValue = default) =>
        Parse(text, startIndex, length, s => func.Invoke(s.Trim()), defaultValue);

    public static string? TrimParse(string text, int startIndex, int length, string defaultValue = "") =>
        TrimParse(text, startIndex, length, s => s, defaultValue);
}
