namespace TinyCinema;

public static class RandomCatalogPicker
{
    public static IReadOnlyList<Movie> GetCandidates(
        MainNavSection nav,
        IReadOnlyList<Movie> allMovies)
    {
        IEnumerable<Movie> query = nav switch
        {
            MainNavSection.Movies => allMovies.Where(movie => !movie.IsTvShow),
            MainNavSection.TvShows => allMovies.Where(movie => movie.IsTvShow),
            MainNavSection.Explore => allMovies,
            MainNavSection.Favorites => allMovies.Where(movie => movie.IsFavorite),
            MainNavSection.Watched => GetWatchedMovies(allMovies),
            _ => []
        };

        return query
            .Where(movie => !string.IsNullOrWhiteSpace(movie.Url))
            .ToList();
    }

    public static string GetEmptyMessage(MainNavSection nav) => nav switch
    {
        MainNavSection.Movies => "No movies in your catalog to pick from.",
        MainNavSection.TvShows => "No TV shows in your catalog to pick from.",
        MainNavSection.Favorites => "Add some favorites first, then roll the dice.",
        MainNavSection.Watched => "Watch something first, then roll for a random rewatch.",
        MainNavSection.Explore => "Your catalog is empty — add movies or TV shows first.",
        _ => "Nothing available to pick right now."
    };

    public static string GetRollingLabel(MainNavSection nav) => nav switch
    {
        MainNavSection.Movies => "Rolling for a random movie...",
        MainNavSection.TvShows => "Rolling for a random TV show...",
        MainNavSection.Favorites => "Rolling through your favorites...",
        MainNavSection.Watched => "Rolling through your watch history...",
        _ => "Rolling the dice..."
    };

    private static List<Movie> GetWatchedMovies(IReadOnlyList<Movie> allMovies)
    {
        var catalogByUrl = allMovies
            .GroupBy(movie => movie.Url, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return WatchedStore.GetAllEntries()
            .Select(entry => WatchedStore.ResolveMovie(entry, catalogByUrl))
            .Where(movie => !string.IsNullOrWhiteSpace(movie.Url))
            .DistinctBy(movie => movie.Url, StringComparer.Ordinal)
            .ToList();
    }
}
