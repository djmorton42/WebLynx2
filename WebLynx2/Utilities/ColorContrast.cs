namespace WebLynx2.Utilities;

public static class ColorContrast
{
    public static string GetReadableTextColor(string? hexColor)
    {
        if (!TryParseRgb(hexColor, out var r, out var g, out var b))
            return "#000000";

        // Relative luminance (sRGB)
        var luminance = (0.2126 * Linearize(r)) + (0.7152 * Linearize(g)) + (0.0722 * Linearize(b));
        return luminance > 0.55 ? "#000000" : "#ffffff";
    }

    public static bool TryParseRgb(string? hexColor, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hexColor))
            return false;

        var hex = hexColor.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length is not (6 or 8))
            return false;

        if (!uint.TryParse(hex[..6], System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return false;

        r = (byte)((rgb >> 16) & 0xFF);
        g = (byte)((rgb >> 8) & 0xFF);
        b = (byte)(rgb & 0xFF);
        return true;
    }

    private static double Linearize(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
