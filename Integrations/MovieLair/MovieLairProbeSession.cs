using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace TinyCinema;

public sealed class MovieLairProbeSession : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _writeLock = new();
    private readonly string _eventsFile;
    private readonly string _networkFile;
    private readonly string _videoSnapshotsFile;
    private readonly HashSet<string> _savedScriptUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _frameLines = [];
    private readonly List<string> _storageOrigins = [];
    private readonly List<string> _summaryNotes = [];
    private int _networkCount;
    private int _eventCount;
    private double _maxObservedCurrentTime;

    public string SessionDirectory { get; }

    public MovieLairProbeSession(
        string title,
        string startUrl,
        string contentType,
        int? season = null,
        int? episode = null)
    {
        var safeTitle = new string(title
            .Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_')
            .ToArray())
            .Trim()
            .Replace(' ', '_');

        if (string.IsNullOrWhiteSpace(safeTitle))
            safeTitle = "session";

        if (safeTitle.Length > 40)
            safeTitle = safeTitle[..40];

        var root = Path.Combine(Path.GetTempPath(), "TinyCinema", "ProbeLogs");
        SessionDirectory = Path.Combine(root, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeTitle}");

        Directory.CreateDirectory(SessionDirectory);
        Directory.CreateDirectory(Path.Combine(SessionDirectory, "network"));
        Directory.CreateDirectory(Path.Combine(SessionDirectory, "network", "js"));
        Directory.CreateDirectory(Path.Combine(SessionDirectory, "storage"));
        Directory.CreateDirectory(Path.Combine(SessionDirectory, "frames"));

        _eventsFile = Path.Combine(SessionDirectory, "events.jsonl");
        _networkFile = Path.Combine(SessionDirectory, "network", "requests.jsonl");
        _videoSnapshotsFile = Path.Combine(SessionDirectory, "video", "snapshots.jsonl");

        Directory.CreateDirectory(Path.Combine(SessionDirectory, "video"));
        Directory.CreateDirectory(Path.Combine(SessionDirectory, "scripts-injected"));

        var sessionInfo = new
        {
            startedAtUtc = DateTime.UtcNow,
            title,
            startUrl,
            contentType,
            season,
            episode,
            sessionDirectory = SessionDirectory
        };

        File.WriteAllText(
            Path.Combine(SessionDirectory, "session.json"),
            JsonSerializer.Serialize(sessionInfo, JsonOptions));

        File.WriteAllText(
            Path.Combine(SessionDirectory, "scripts-injected", "probe-bootstrap.js"),
            MovieLairProbeScript.ProbeBootstrapScript);

        LogEvent("session-started", sessionInfo);
    }

    public void LogEvent(string type, object? payload = null)
    {
        var line = JsonSerializer.Serialize(new
        {
            atUtc = DateTime.UtcNow,
            type,
            payload
        }, CompactJsonOptions);

        lock (_writeLock)
        {
            File.AppendAllText(_eventsFile, line + Environment.NewLine, Encoding.UTF8);
            _eventCount++;
        }
    }

    public void LogFrameCreated(CoreWebView2Frame frame)
    {
        var entry = $"{DateTime.UtcNow:O}\t{frame.Name}\t_created_";
        _frameLines.Add(entry);
        LogEvent("frame-created", new { frameName = frame.Name });

        frame.NavigationCompleted += (_, args) =>
        {
            var url = "unknown";
            try
            {
                url = frame.Name;
            }
            catch
            {
                // Frame URL may be unavailable for cross-origin frames.
            }

            LogEvent("frame-navigation", new
            {
                frameName = frame.Name,
                isSuccess = args.IsSuccess,
                httpStatusCode = args.HttpStatusCode,
                webErrorStatus = args.WebErrorStatus.ToString()
            });
        };
    }

    public void LogNavigation(string phase, string url, bool isSuccess, int httpStatusCode)
    {
        LogEvent("navigation", new
        {
            phase,
            url,
            isSuccess,
            httpStatusCode
        });
    }

    public void LogNetworkRequest(CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var url = e.Request.Uri;
        if (string.IsNullOrWhiteSpace(url))
            return;

        var resourceType = GuessResourceType(url);

        int? statusCode = null;
        try
        {
            statusCode = e.Response.StatusCode;
        }
        catch
        {
            // Ignore.
        }

        var line = JsonSerializer.Serialize(new
        {
            atUtc = DateTime.UtcNow,
            url,
            resourceType,
            statusCode
        }, CompactJsonOptions);

        lock (_writeLock)
        {
            File.AppendAllText(_networkFile, line + Environment.NewLine, Encoding.UTF8);
            _networkCount++;
        }
    }

    public async Task TrySaveScriptResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        const int maxBytes = 512 * 1024;

        var url = e.Request.Uri;
        if (string.IsNullOrWhiteSpace(url))
            return;

        var isScript = url.Contains(".js", StringComparison.OrdinalIgnoreCase);
        var isDocument = url.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                         url.Contains("watch-tv/", StringComparison.OrdinalIgnoreCase) ||
                         url.Contains("watch-movie", StringComparison.OrdinalIgnoreCase) ||
                         url.Contains("movielair", StringComparison.OrdinalIgnoreCase) ||
                         url.Contains("tinyzone", StringComparison.OrdinalIgnoreCase);

        if (!isScript && !isDocument)
            return;

        if (!_savedScriptUrls.Add(url))
            return;

        try
        {
            using var contentStream = await e.Response.GetContentAsync();
            using var memory = new MemoryStream();
            await contentStream.CopyToAsync(memory);

            if (memory.Length == 0 || memory.Length > maxBytes)
            {
                LogEvent("network-body-skipped", new { url, bytes = memory.Length });
                return;
            }

            var folder = isDocument ? "network" : Path.Combine("network", "js");
            var targetDirectory = Path.Combine(SessionDirectory, folder);
            Directory.CreateDirectory(targetDirectory);

            var fileName = BuildSafeFileName(url, isDocument ? ".html" : ".js");
            var targetPath = Path.Combine(targetDirectory, fileName);
            await File.WriteAllBytesAsync(targetPath, memory.ToArray());

            LogEvent("network-body-saved", new { url, path = targetPath, bytes = memory.Length });
        }
        catch (Exception ex)
        {
            LogEvent("network-body-error", new { url, error = ex.Message });
        }
    }

    public void HandleWebMessage(string messageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "unknown" : "unknown";

            if (type is "video-snapshot" or "media-play" or "media-pause" or "media-seeked" or "heartbeat" or "manual-snapshot")
                TrackVideoPayload(root);

            if (type.StartsWith("storage", StringComparison.Ordinal) &&
                root.TryGetProperty("origin", out var originProp))
            {
                var origin = originProp.GetString();
                if (!string.IsNullOrWhiteSpace(origin) && _storageOrigins.All(o => !o.Equals(origin, StringComparison.OrdinalIgnoreCase)))
                    _storageOrigins.Add(origin);
            }

            if (type is "storage-snapshot" or "manual-snapshot")
                SaveStorageSnapshot(root);

            lock (_writeLock)
            {
                File.AppendAllText(_eventsFile, messageJson + Environment.NewLine, Encoding.UTF8);
                _eventCount++;
            }
        }
        catch (Exception ex)
        {
            LogEvent("webmessage-parse-error", new { error = ex.Message, raw = messageJson });
        }
    }

    public async Task RequestSnapshotAsync(CoreWebView2 coreWebView)
    {
        try
        {
            await coreWebView.ExecuteScriptAsync(MovieLairProbeScript.RequestSnapshotScript);
            LogEvent("snapshot-requested", new { source = "main-frame" });
        }
        catch (Exception ex)
        {
            LogEvent("snapshot-request-failed", new { error = ex.Message });
        }
    }

    public void WriteSummary()
    {
        var summaryPath = Path.Combine(SessionDirectory, "summary.txt");
        var builder = new StringBuilder();
        builder.AppendLine("TinyCinema MovieLair Probe Summary");
        builder.AppendLine("==================================");
        builder.AppendLine($"Session folder: {SessionDirectory}");
        builder.AppendLine($"Events logged: {_eventCount}");
        builder.AppendLine($"Network requests logged: {_networkCount}");
        builder.AppendLine($"Storage origins seen: {_storageOrigins.Count}");
        builder.AppendLine($"Max observed currentTime (seconds): {_maxObservedCurrentTime:0.##}");
        builder.AppendLine();

        if (_storageOrigins.Count > 0)
        {
            builder.AppendLine("Origins:");
            foreach (var origin in _storageOrigins)
                builder.AppendLine($"  - {origin}");
            builder.AppendLine();
        }

        foreach (var note in _summaryNotes)
            builder.AppendLine(note);

        builder.AppendLine();
        builder.AppendLine("Next steps:");
        builder.AppendLine("  1. Open events.jsonl and search for currentTime, storage-set, storage-get, resume, progress.");
        builder.AppendLine("  2. Inspect network/js for embed player scripts.");
        builder.AppendLine("  3. Share this folder for analysis.");

        File.WriteAllText(summaryPath, builder.ToString(), Encoding.UTF8);
        LogEvent("session-ended", new { summaryPath });
    }

    public void Dispose()
    {
        WriteSummary();

        if (_frameLines.Count > 0)
        {
            File.WriteAllLines(Path.Combine(SessionDirectory, "frames", "frame-events.txt"), _frameLines);
        }
    }

    private void TrackVideoPayload(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload))
            return;

        IEnumerable<JsonElement> mediaItems = [];

        if (payload.TryGetProperty("media", out var mediaArray) && mediaArray.ValueKind == JsonValueKind.Array)
            mediaItems = mediaArray.EnumerateArray();
        else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("media", out var nestedMedia) && nestedMedia.ValueKind == JsonValueKind.Array)
            mediaItems = nestedMedia.EnumerateArray();

        foreach (var media in mediaItems)
        {
            if (media.TryGetProperty("currentTime", out var currentTimeProp) &&
                currentTimeProp.TryGetDouble(out var currentTime) &&
                currentTime > _maxObservedCurrentTime)
            {
                _maxObservedCurrentTime = currentTime;
            }
        }

        var line = root.GetRawText();
        lock (_writeLock)
        {
            File.AppendAllText(_videoSnapshotsFile, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private void SaveStorageSnapshot(JsonElement root)
    {
        if (!root.TryGetProperty("payload", out var payload))
            return;

        var origin = root.TryGetProperty("origin", out var originProp) ? originProp.GetString() ?? "unknown" : "unknown";
        var safeOrigin = BuildSafeFileName(origin, ".json");
        var path = Path.Combine(SessionDirectory, "storage", safeOrigin);

        try
        {
            File.WriteAllText(path, payload.GetRawText());
        }
        catch (Exception ex)
        {
            LogEvent("storage-save-error", new { origin, error = ex.Message });
        }
    }

    private static string GuessResourceType(string url)
    {
        if (url.Contains(".js", StringComparison.OrdinalIgnoreCase))
            return "script";
        if (url.Contains(".html", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".htm", StringComparison.OrdinalIgnoreCase))
            return "document";
        if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            return "media";
        if (url.Contains(".css", StringComparison.OrdinalIgnoreCase))
            return "stylesheet";
        return "other";
    }

    private static string BuildSafeFileName(string url, string extension)
    {
        var name = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            name = absolute.Host + absolute.AbsolutePath;

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        cleaned = cleaned.Replace('/', '_').Replace(':', '_');

        if (cleaned.Length > 80)
            cleaned = cleaned[..80];

        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "resource";

        return cleaned + extension;
    }
}
