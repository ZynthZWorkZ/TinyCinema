namespace TinyCinema;

public sealed class SearchIndexData
{
    public const int VectorDimension = 384;

    public required string[] Urls { get; init; }

    public required float[][] Vectors { get; init; }

    public required DateTime CatalogLastWriteUtc { get; init; }

    public required string ModelName { get; init; }

    private Dictionary<string, int>? _urlToIndex;

    public bool TryGetVector(string url, out float[] vector)
    {
        vector = Array.Empty<float>();
        if (string.IsNullOrWhiteSpace(url))
            return false;

        _urlToIndex ??= BuildUrlIndex();
        if (!_urlToIndex.TryGetValue(url, out var index))
            return false;

        vector = Vectors[index];
        return true;
    }

    private Dictionary<string, int> BuildUrlIndex()
    {
        var map = new Dictionary<string, int>(Urls.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Urls.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(Urls[i]))
                map[Urls[i]] = i;
        }

        return map;
    }
}
