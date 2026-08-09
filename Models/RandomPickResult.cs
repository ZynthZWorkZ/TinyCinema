namespace TinyCinema;

public enum RandomPickAction
{
    Watch,
    RollAgain,
    Cancelled
}

public sealed class RandomPickResult
{
    public RandomPickAction Action { get; init; }

    public Movie? Movie { get; init; }
}
