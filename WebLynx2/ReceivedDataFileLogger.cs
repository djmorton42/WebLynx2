using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace WebLynx2;

/// <summary>
/// Appends received TCP payloads to per-channel log files under <c>received_data</c>.
/// Each entry records raw bytes (hex) and a UTF-16 LE textual view of the same payload.
/// </summary>
public sealed class ReceivedDataFileLogger
{
    private readonly string _clockPath;
    private readonly string _resultsPath;
    private readonly object _clockLock = new();
    private readonly object _resultsLock = new();

    public ReceivedDataFileLogger(string? baseDirectory = null)
    {
        var root = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "received_data");
        Directory.CreateDirectory(root);
        _clockPath = Path.Combine(root, "clock.dat");
        _resultsPath = Path.Combine(root, "results.dat");
    }

    public void LogClock(ReadOnlySpan<byte> data) => Log(_clockPath, _clockLock, data);

    public void LogResults(ReadOnlySpan<byte> data) => Log(_resultsPath, _resultsLock, data);

    private static void Log(string path, object gate, ReadOnlySpan<byte> data)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var hex = ToSpacedHex(data);
        var text = Utf16LeVisualString(data);

        var block = new StringBuilder(64 + hex.Length + text.Length);
        block.AppendLine($"--- {stamp} (UTC) ---");
        block.Append("Raw (hex): ");
        block.AppendLine(hex);
        block.Append("Text (UTF-16 LE): ");
        block.AppendLine(text);
        block.AppendLine();

        var bytes = Encoding.UTF8.GetBytes(block.ToString());

        lock (gate)
        {
            using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
            stream.Write(bytes);
        }
    }

    private static string ToSpacedHex(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return "";

        var sb = new StringBuilder(data.Length * 3 - 1);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Decodes payload as UTF-16 LE code units; shows C-style escapes for controls and non-characters.
    /// </summary>
    private static string Utf16LeVisualString(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return "";

        var charCount = data.Length / 2;
        var decoded = charCount == 0
            ? ""
            : Encoding.Unicode.GetString(data[..(charCount * 2)]);

        var sb = new StringBuilder(decoded.Length + 8);
        foreach (var ch in decoded)
        {
            switch (ch)
            {
                case '\0':
                    sb.Append("\\0");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch) || char.GetUnicodeCategory(ch) == UnicodeCategory.Surrogate)
                        sb.Append(CultureInfo.InvariantCulture, $"\\u{(uint)ch:X4}");
                    else
                        sb.Append(ch);
                    break;
            }
        }

        if (data.Length % 2 != 0)
            sb.Append(CultureInfo.InvariantCulture, $" [+{data[^1]:X2} trailing byte]");

        return sb.ToString();
    }
}
