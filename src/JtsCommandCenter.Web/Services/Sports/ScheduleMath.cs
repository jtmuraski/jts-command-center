namespace JtsCommandCenter.Web.Services.Sports;

/// <summary>Pure schedule logic (no I/O), kept separate so it's easy to reason about and test.</summary>
public static class ScheduleMath
{
    /// <summary>Most recent completed game.</summary>
    public static Game? LastGame(IReadOnlyList<Game> games) =>
        games.LastOrDefault(g => g.State == GameState.Final);

    /// <summary>The game in progress, or else the next scheduled game.</summary>
    public static Game? NextGame(IReadOnlyList<Game> games) =>
        games.FirstOrDefault(g => g.State == GameState.Live)
        ?? games.FirstOrDefault(g => g.State == GameState.Scheduled);

    /// <summary>Groups date-ordered games into series: back-to-back games against the same opponent at the same park.</summary>
    public static List<Series> GroupSeries(IReadOnlyList<Game> games)
    {
        var series = new List<Series>();
        var current = new List<Game>();

        foreach (var game in games)
        {
            if (current.Count > 0)
            {
                var previous = current[^1];
                var sameSeries = previous.Opponent.Abbreviation == game.Opponent.Abbreviation
                    && previous.IsHome == game.IsHome
                    && previous.IsPostseason == game.IsPostseason;

                if (!sameSeries)
                {
                    series.Add(ToSeries(current));
                    current = [];
                }
            }

            current.Add(game);
        }

        if (current.Count > 0)
        {
            series.Add(ToSeries(current));
        }

        return series;
    }

    /// <summary>
    /// Picks the series the team is "in" today and the one after it. Between series (an off day, or the
    /// morning before a new series starts) the series that just finished stays current until the next
    /// one's first game day.
    /// </summary>
    public static (Series? Current, Series? Next) CurrentAndNextSeries(IReadOnlyList<Series> series, DateOnly today)
    {
        var upcoming = series.FindIndex(s => s.Games.Any(g => g.State is GameState.Live or GameState.Scheduled));
        var played = series.FindLastIndex(s => s.Games.Any(g => g.State == GameState.Final));

        if (upcoming < 0)
        {
            return (played < 0 ? null : series[played], null);
        }

        var startsNewSeries = played < upcoming && played >= 0 && series[upcoming].FirstDay > today;
        var current = startsNewSeries ? played : upcoming;
        var next = startsNewSeries ? upcoming : upcoming + 1;

        return (series[current], next < series.Count ? series[next] : null);
    }

    private static Series ToSeries(List<Game> games)
    {
        var first = games[0];
        return new Series(first.Opponent, first.IsHome, first.IsPostseason, games);
    }
}

file static class ListExtensions
{
    public static int FindIndex<T>(this IReadOnlyList<T> list, Func<T, bool> match)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (match(list[i])) return i;
        }
        return -1;
    }

    public static int FindLastIndex<T>(this IReadOnlyList<T> list, Func<T, bool> match)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (match(list[i])) return i;
        }
        return -1;
    }
}
