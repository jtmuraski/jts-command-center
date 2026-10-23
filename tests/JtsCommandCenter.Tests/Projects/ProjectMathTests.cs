using JtsCommandCenter.Core.Projects;

namespace JtsCommandCenter.Tests.Projects;

public class ProjectMathTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);

    private static Subtask Sub(SubtaskStatus status) => new() { Status = status };

    [Fact]
    public void ProgressOf_CountsDoneSubtasks()
    {
        var progress = ProjectMath.ProgressOf(
            [Sub(SubtaskStatus.Done), Sub(SubtaskStatus.InProgress), Sub(SubtaskStatus.Blocked)]);

        Assert.Equal(new ProjectProgress(1, 3), progress);
        Assert.Equal(33, progress.Percent);
    }

    [Fact]
    public void ProgressOf_NoSubtasks_IsZeroPercent() =>
        Assert.Equal(0, ProjectMath.ProgressOf([]).Percent);

    [Theory]
    [InlineData(-1, false, true)]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(-1, true, false)]
    public void IsOverdue_OnlyPastDueAndUnfinished(int dueOffset, bool done, bool expected) =>
        Assert.Equal(expected, ProjectMath.IsOverdue(Today.AddDays(dueOffset), done, Today));

    [Fact]
    public void IsOverdue_NoDueDate_IsNeverOverdue() =>
        Assert.False(ProjectMath.IsOverdue((DateOnly?)null, false, Today));

    [Fact]
    public void IsOverdue_UsesEachItemsDoneStatus()
    {
        var due = Today.AddDays(-3);

        Assert.True(ProjectMath.IsOverdue(new Project { DueDate = due, Status = ProjectStatus.OnHold }, Today));
        Assert.False(ProjectMath.IsOverdue(new Project { DueDate = due, Status = ProjectStatus.Done }, Today));
        Assert.True(ProjectMath.IsOverdue(new Subtask { DueDate = due, Status = SubtaskStatus.Blocked }, Today));
        Assert.False(ProjectMath.IsOverdue(new Subtask { DueDate = due, Status = SubtaskStatus.Done }, Today));
    }

    [Theory]
    [InlineData(0, "x,a,b,c")]
    [InlineData(2, "a,b,x,c")]
    [InlineData(3, "a,b,c,x")]
    [InlineData(99, "a,b,c,x")]
    [InlineData(-5, "x,a,b,c")]
    public void Reorder_InsertsAtClampedIndex(int index, string expected) =>
        Assert.Equal(expected, string.Join(",", ProjectMath.Reorder(["a", "b", "c"], "x", index)));

    // Full column a..e; the filter shows only b and d.
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    public void MapIndex_PlacesTheCardRelativeToVisibleNeighbors(int visibleIndex, int expected) =>
        Assert.Equal(expected, ProjectMath.MapIndex(["a", "b", "c", "d", "e"], ["b", "d"], visibleIndex));

    [Fact]
    public void MapIndex_NothingVisible_GoesToTheEnd() =>
        Assert.Equal(3, ProjectMath.MapIndex(["a", "b", "c"], [], 0));

    [Fact]
    public void MapIndex_Unfiltered_IsTheSameIndex() =>
        Assert.Equal(2, ProjectMath.MapIndex(["a", "b", "c"], ["a", "b", "c"], 2));

    [Fact]
    public void Validate_AcceptsTitleAndOrderedDates() =>
        Assert.Empty(ProjectMath.Validate("Workflow update", Today, Today.AddDays(30)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RequiresTitle(string? title) =>
        Assert.Equal(["Title is required."], ProjectMath.Validate(title, null, null));

    [Fact]
    public void Validate_RejectsLongTitle() =>
        Assert.Single(ProjectMath.Validate(new string('x', ProjectMath.MaxTitleLength + 1), null, null));

    [Fact]
    public void Validate_RejectsDueBeforeStart() =>
        Assert.Equal(["Due date can't be before the start date."], ProjectMath.Validate("Gutters", Today, Today.AddDays(-1)));

    [Theory]
    [InlineData("https://dev.azure.com/org/project/_workitems/edit/42", true)]
    [InlineData("  http://example.com/spec  ", true)]
    [InlineData("ftp://example.com/file", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParseLink_AcceptsOnlyAbsoluteHttpUrls(string? url, bool valid) =>
        Assert.Equal(valid, ProjectMath.ParseLink(url) is not null);

    [Fact]
    public void DisplayName_UsesBoardLabels()
    {
        Assert.Equal("On Hold", ProjectMath.DisplayName(ProjectStatus.OnHold));
        Assert.Equal("To Do", ProjectMath.DisplayName(SubtaskStatus.ToDo));
        Assert.Equal("In Progress", ProjectMath.DisplayName(SubtaskStatus.InProgress));
    }

    [Fact]
    public void StatusChangeText_AppendsReason()
    {
        Assert.Equal("Moved from To Do to In Progress", ProjectMath.StatusChangeText("To Do", "In Progress", null));
        Assert.Equal("Moved from In Progress to Blocked: RFI submitted to management",
            ProjectMath.StatusChangeText("In Progress", "Blocked", "RFI submitted to management"));
    }
}
