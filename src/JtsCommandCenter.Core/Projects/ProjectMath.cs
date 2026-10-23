namespace JtsCommandCenter.Core.Projects;

/// <summary>Subtasks done out of the total.</summary>
public readonly record struct ProjectProgress(int Done, int Total)
{
    public int Percent => Total == 0 ? 0 : Done * 100 / Total;
}

/// <summary>Pure project logic (no I/O): progress, due dates, board ordering, validation, and display text.</summary>
public static class ProjectMath
{
    public const int MaxTitleLength = 200;
    public const int MaxReasonLength = 500;
    public const int MaxLabelLength = 200;
    public const int MaxUrlLength = 2048;

    public static ProjectProgress ProgressOf(IEnumerable<Subtask> subtasks)
    {
        var done = 0;
        var total = 0;
        foreach (var subtask in subtasks)
        {
            total++;
            if (subtask.Status == SubtaskStatus.Done)
            {
                done++;
            }
        }

        return new ProjectProgress(done, total);
    }

    /// <summary>Past due and not finished. Something due today isn't overdue yet.</summary>
    public static bool IsOverdue(DateOnly? dueDate, bool done, DateOnly today) => !done && dueDate < today;

    public static bool IsOverdue(Project project, DateOnly today) =>
        IsOverdue(project.DueDate, project.Status == ProjectStatus.Done, today);

    public static bool IsOverdue(Subtask subtask, DateOnly today) =>
        IsOverdue(subtask.DueDate, subtask.Status == SubtaskStatus.Done, today);

    /// <summary>
    /// The column's items in their new order after inserting <paramref name="item"/> at
    /// <paramref name="index"/> (clamped to the column). <paramref name="column"/> must not contain the item.
    /// </summary>
    public static List<T> Reorder<T>(IReadOnlyList<T> column, T item, int index)
    {
        var ordered = column.ToList();
        ordered.Insert(Math.Clamp(index, 0, ordered.Count), item);
        return ordered;
    }

    /// <summary>Problems with a title and date range, empty when valid.</summary>
    public static IReadOnlyList<string> Validate(string? title, DateOnly? startDate, DateOnly? dueDate)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("Title is required.");
        }
        else if (title.Trim().Length > MaxTitleLength)
        {
            errors.Add($"Title can be at most {MaxTitleLength} characters.");
        }

        if (startDate > dueDate)
        {
            errors.Add("Due date can't be before the start date.");
        }

        return errors;
    }

    /// <summary>The URL as an absolute http(s) URI, or null when it isn't one.</summary>
    public static Uri? ParseLink(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.OriginalString.Length <= MaxUrlLength
            ? uri
            : null;

    /// <summary>Trimmed text, or null when blank.</summary>
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static string DisplayName(ProjectStatus status) => status switch
    {
        ProjectStatus.Planned => "Planned",
        ProjectStatus.Active => "Active",
        ProjectStatus.OnHold => "On Hold",
        ProjectStatus.Done => "Done",
        _ => status.ToString(),
    };

    public static string DisplayName(SubtaskStatus status) => status switch
    {
        SubtaskStatus.ToDo => "To Do",
        SubtaskStatus.InProgress => "In Progress",
        SubtaskStatus.Blocked => "Blocked",
        SubtaskStatus.Done => "Done",
        _ => status.ToString(),
    };

    /// <summary>Activity-log text for a status change, e.g. "Moved from To Do to Blocked: waiting on IT".</summary>
    public static string StatusChangeText(string from, string to, string? reason)
    {
        var text = $"Moved from {from} to {to}";
        return reason is null ? text : $"{text}: {reason}";
    }
}
