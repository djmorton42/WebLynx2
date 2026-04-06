using Microsoft.Extensions.Logging;

namespace WebLynx2.Parsing;

/// <summary>
/// Parses lap count strings from fixed-width timing lines (whole numbers and "N 1/2" half-lap notation).
/// </summary>
public class LapCountParser
{
    private readonly ILogger<LapCountParser> _logger;

    public LapCountParser(ILogger<LapCountParser> logger)
    {
        _logger = logger;
    }

    public decimal Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0m;

        var trimmedText = text.Trim();
        _logger.LogDebug("Parse input: '{Text}' -> trimmed: '{TrimmedText}'", text, trimmedText);

        if (trimmedText.Contains("1/2"))
        {
            var numberPart = trimmedText.Replace("1/2", "").Trim();
            _logger.LogDebug("Half lap - numberPart: '{NumberPart}'", numberPart);
            if (int.TryParse(numberPart, out var wholeNumber))
            {
                var result = wholeNumber + 0.5m;
                _logger.LogDebug("Half lap result: {Result}", result);
                return result;
            }

            _logger.LogDebug("Half lap - just 1/2, returning 0.5");
            return 0.5m;
        }

        if (decimal.TryParse(trimmedText, out var decimalResult))
        {
            _logger.LogDebug("Decimal result: {Result}", decimalResult);
            return decimalResult;
        }

        _logger.LogDebug("Invalid input, returning 0");
        return 0m;
    }
}
