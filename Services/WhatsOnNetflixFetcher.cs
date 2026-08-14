using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyCinema;

public sealed class WhatsOnFetchProgress
{
    public int CurrentPage { get; init; }

    public int TotalPages { get; init; }

    public int ItemsLoaded { get; init; }

    public string Status { get; init; } = string.Empty;
}

public static class WhatsOnNetflixFetcher
{
    private const string BaseUrl = "https://www.whats-on-netflix.com/wp-json/won-library/v1/titles";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private const string WonReferer = "https://www.whats-on-netflix.com/library/movies/";
    private const int PerPage = 60;
    private const int RequestDelayMs = 300;

    private static string? _activeCurlBin;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static async Task<WhatsOnNetflixCatalog> FetchAllMoviesAsync(
        IProgress<WhatsOnFetchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new WhatsOnFetchProgress
        {
            CurrentPage = 1,
            TotalPages = 1,
            Status = "Fetching Netflix movie listings..."
        });

        var firstPage = await FetchPageAsync(1, cancellationToken);
        var totalPages = Math.Max(1, firstPage.Pages);
        var items = new List<WhatsOnNetflixItem>(firstPage.Total);
        items.AddRange(firstPage.Items);

        progress?.Report(new WhatsOnFetchProgress
        {
            CurrentPage = 1,
            TotalPages = totalPages,
            ItemsLoaded = items.Count,
            Status = $"Loaded page 1 of {totalPages} ({items.Count:N0} movies)..."
        });

        for (var page = 2; page <= totalPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageResult = await FetchPageAsync(page, cancellationToken);
            items.AddRange(pageResult.Items);

            progress?.Report(new WhatsOnFetchProgress
            {
                CurrentPage = page,
                TotalPages = totalPages,
                ItemsLoaded = items.Count,
                Status = $"Loaded page {page} of {totalPages} ({items.Count:N0} movies)..."
            });

            if (page < totalPages)
                await Task.Delay(RequestDelayMs, cancellationToken);
        }

        return new WhatsOnNetflixCatalog
        {
            FetchedAt = DateTime.UtcNow,
            Source = $"{BaseUrl}?type=Movie",
            Total = firstPage.Total,
            Count = items.Count,
            Items = items
        };
    }

    private static async Task<WonApiPageResponse> FetchPageAsync(int page, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}?type=Movie&per_page={PerPage}&page={page}";
        var raw = await FetchJsonViaCurlAsync(url, cancellationToken);

        var pageResult = JsonSerializer.Deserialize<WonApiPageResponse>(raw, JsonOptions)
            ?? throw new InvalidOperationException($"Empty response while fetching page {page}.");

        pageResult.Items ??= [];
        return pageResult;
    }

    private static async Task<string> FetchJsonViaCurlAsync(string url, CancellationToken cancellationToken)
    {
        var tmpFile = Path.Combine(Path.GetTempPath(), $"tinycinema-won-{Guid.NewGuid():N}.json");
        var tried = new List<string>();
        Exception? lastError = null;

        foreach (var curlBin in GetCurlCandidates())
        {
            if (tried.Contains(curlBin, StringComparer.OrdinalIgnoreCase))
                continue;

            tried.Add(curlBin);

            try
            {
                await RunCurlAsync(curlBin, url, tmpFile, cancellationToken);
                var raw = await File.ReadAllTextAsync(tmpFile, cancellationToken);

                if (IsCloudflareChallenge(raw))
                {
                    lastError = new InvalidOperationException($"Cloudflare challenge from {curlBin}");
                    continue;
                }

                _activeCurlBin = curlBin;
                return raw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            finally
            {
                if (File.Exists(tmpFile))
                {
                    try { File.Delete(tmpFile); } catch { /* ignore */ }
                }
            }
        }

        var triedList = tried.Count > 0 ? string.Join(", ", tried) : "none";
        throw new InvalidOperationException(
            $"Blocked by Cloudflare while fetching Netflix listings. Tried: {triedList}. " +
            $"Last error: {lastError?.Message ?? "unknown"}");
    }

    private static async Task RunCurlAsync(
        string curlBin,
        string url,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = curlBin,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-sS");
        startInfo.ArgumentList.Add("--compressed");
        startInfo.ArgumentList.Add("-A");
        startInfo.ArgumentList.Add(UserAgent);
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add("Accept: application/json, text/plain, */*");
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add("Accept-Language: en-US,en;q=0.9");
        startInfo.ArgumentList.Add("-H");
        startInfo.ArgumentList.Add($"Referer: {WonReferer}");
        startInfo.ArgumentList.Add(url);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {curlBin}.");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{curlBin} failed (exit {process.ExitCode}): {stderr.Trim()}");
    }

    private static IEnumerable<string> GetCurlCandidates()
    {
        var envBin = Environment.GetEnvironmentVariable("TINYCINEMA_CURL")
            ?? Environment.GetEnvironmentVariable("TELEROKU_CURL")
            ?? Environment.GetEnvironmentVariable("CURL_BIN");

        if (!string.IsNullOrWhiteSpace(envBin))
            yield return envBin.Trim();

        if (_activeCurlBin != null)
            yield return _activeCurlBin;

        if (OperatingSystem.IsWindows())
        {
            yield return "curl.exe";
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/opt/curl/bin/curl";
            yield return "/opt/homebrew/bin/curl";
            yield return "/usr/local/opt/curl/bin/curl";
            yield return "/usr/local/bin/curl";
        }

        yield return "curl";
        yield return "/usr/bin/curl";
    }

    private static bool IsCloudflareChallenge(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return text.Contains("Just a moment", StringComparison.Ordinal) ||
               text.Contains("cf-browser-verification", StringComparison.Ordinal) ||
               text.Contains("challenge-platform", StringComparison.Ordinal);
    }

    private sealed class WonApiPageResponse
    {
        public int Page { get; set; }

        public int Pages { get; set; }

        public int Total { get; set; }

        [JsonPropertyName("per_page")]
        public int PerPage { get; set; }

        public List<WhatsOnNetflixItem> Items { get; set; } = [];
    }
}
