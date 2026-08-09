namespace TinyCinema;

public sealed class SearchPassageField
{
    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

public sealed class SearchPassageDescription
{
    public IReadOnlyList<SearchPassageField> Fields { get; init; } = [];

    public string Preview { get; init; } = string.Empty;

    public IReadOnlyList<string> SearchKeywords { get; init; } = [];

    public IReadOnlyList<string> ModelTokens { get; init; } = [];
}
