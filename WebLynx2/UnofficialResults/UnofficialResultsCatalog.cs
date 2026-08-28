using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebLynx2.UnofficialResults;

/// <summary>
/// In-memory catalog of complete unofficial race results, refreshed by polling a directory of LIF files.
/// Thread-safe for concurrent HTTP reads while polling.
/// </summary>
public sealed class UnofficialResultsCatalog
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, UnofficialRaceResult> _results = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _fileHashes = new(StringComparer.Ordinal);

    public UnofficialResultsCatalog(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public Encoding FileEncoding { get; set; } = Encoding.GetEncoding("ISO-8859-1");

    public int Count => _results.Count;

    public void Clear()
    {
        _results.Clear();
        _fileHashes.Clear();
    }

    /// <summary>
    /// Scans <paramref name="directoryPath"/> for <c>*.lif</c> files (top-level only),
    /// parses changed files, and stores only complete races.
    /// </summary>
    public async Task RefreshAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            _logger.LogDebug("Unofficial results directory does not exist: {Path}", directoryPath);
            return;
        }

        var lifFiles = Directory.GetFiles(directoryPath, "*.lif", SearchOption.TopDirectoryOnly);

        foreach (var filePath in lifFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TryProcessFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        PruneMissingFiles(lifFiles);
    }

    public UnofficialRaceResult? GetLatestRace() =>
        _results.Values
            .OrderByDescending(r => r.RaceStartTime ?? DateTime.MinValue)
            .FirstOrDefault();

    public List<UnofficialRaceInfo> GetAllRaceInfo() =>
        _results.Values
            .OrderByDescending(r => r.RaceStartTime ?? DateTime.MinValue)
            .Select(r => new UnofficialRaceInfo
            {
                RaceNumber = r.RaceNumber,
                Heat = r.Heat,
                Round = r.Round,
                EventName = r.EventName,
                StartTime = r.StartTime,
                RaceStartTime = r.RaceStartTime,
                RacerCount = r.Racers.Count
            })
            .ToList();

    public UnofficialRaceResult? GetRaceByNumber(string raceNumber)
    {
        _results.TryGetValue(raceNumber, out var result);
        return result;
    }

    /// <summary>
    /// Test helper: insert a complete result without going through the filesystem.
    /// </summary>
    public void UpsertForTests(UnofficialRaceResult result)
    {
        _results.AddOrUpdate(result.RaceNumber, result, (_, _) => result);
        if (!string.IsNullOrEmpty(result.FilePath))
            _fileHashes[result.FilePath] = "test";
    }

    private async Task TryProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);
        try
        {
            string currentHash;
            try
            {
                currentHash = await ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "File is locked or inaccessible, skipping: {FileName}", fileName);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "No permission to read file, skipping: {FileName}", fileName);
                return;
            }

            if (_fileHashes.TryGetValue(filePath, out var previousHash) && previousHash == currentHash)
                return;

            var raceResult = await ParseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (raceResult is null)
                return;

            if (!LifFileParser.IsResultsComplete(raceResult))
            {
                _logger.LogDebug(
                    "Results incomplete for race {RaceNumber} in {FileName}, ignoring",
                    raceResult.RaceNumber,
                    fileName);
                return;
            }

            _results.AddOrUpdate(raceResult.RaceNumber, raceResult, (_, _) => raceResult);
            _fileHashes.AddOrUpdate(filePath, currentHash, (_, _) => currentHash);
            _logger.LogInformation(
                "Loaded unofficial results for race {RaceNumber} from {FileName}",
                raceResult.RaceNumber,
                fileName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing LIF file, skipping: {FilePath}", filePath);
        }
    }

    private async Task<UnofficialRaceResult?> ParseFileAsync(string filePath, CancellationToken cancellationToken)
    {
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(filePath, FileEncoding, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Cannot read LIF file (may be locked): {FilePath}", filePath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "No permission to read LIF file: {FilePath}", filePath);
            return null;
        }

        DateTime lastWriteTime;
        try
        {
            lastWriteTime = File.GetLastWriteTimeUtc(filePath);
        }
        catch
        {
            lastWriteTime = DateTime.UtcNow;
        }

        return LifFileParser.ParseLines(lines, filePath, lastWriteTime);
    }

    private void PruneMissingFiles(string[] lifFiles)
    {
        var existingFiles = new HashSet<string>(lifFiles, StringComparer.Ordinal);
        var toRemove = _results
            .Where(kvp => !existingFiles.Contains(kvp.Value.FilePath))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var raceNumber in toRemove)
        {
            if (_results.TryRemove(raceNumber, out var removed))
            {
                _fileHashes.TryRemove(removed.FilePath, out _);
                _logger.LogInformation(
                    "Removed unofficial results for race {RaceNumber} (file no longer exists)",
                    raceNumber);
            }
        }
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(hash);
    }
}
