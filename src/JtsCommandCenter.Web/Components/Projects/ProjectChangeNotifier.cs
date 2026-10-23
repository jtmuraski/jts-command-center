namespace JtsCommandCenter.Web.Components.Projects;

/// <summary>
/// Lets components on the same page tell each other that project data changed (e.g. a task checked off
/// on the dashboard's Due Today card, so the Projects card reloads its progress). One per circuit.
/// </summary>
public sealed class ProjectChangeNotifier
{
    /// <summary>Raised after a change is saved. Handlers run on the caller's thread; marshal with InvokeAsync.</summary>
    public event Func<Task>? Changed;

    public async Task NotifyChangedAsync()
    {
        if (Changed is null)
        {
            return;
        }

        foreach (var handler in Changed.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }
}
