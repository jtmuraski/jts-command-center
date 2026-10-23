namespace JtsCommandCenter.Core.News;

public static class NewsMath
{
    /// <summary>
    /// Picks the top stories across categories for the dashboard: each category's newest story first (in
    /// category order), then each one's second-newest, and so on, so one busy category can't crowd out
    /// the rest. A story that already appeared under an earlier category is skipped.
    /// </summary>
    public static IReadOnlyList<NewsHighlight> Highlights(NewsReport report, int perCategory)
    {
        var picks = new List<NewsHighlight>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cursors = new int[report.Categories.Count];
        var counts = new int[report.Categories.Count];

        bool added;
        do
        {
            added = false;
            for (var i = 0; i < report.Categories.Count; i++)
            {
                var category = report.Categories[i];
                while (counts[i] < perCategory && cursors[i] < category.Articles.Count)
                {
                    var article = category.Articles[cursors[i]++];
                    if (seen.Add(article.Url))
                    {
                        picks.Add(new NewsHighlight(category.Key, category.Name, article));
                        counts[i]++;
                        added = true;
                        break;
                    }
                }
            }
        }
        while (added);

        return picks;
    }
}
