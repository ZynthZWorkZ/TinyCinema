namespace TinyCinema;

public class LiveUrlEntry
{
    public required string Url { get; init; }
    public bool IsStream { get; init; }
    public required string Time { get; init; }
}
