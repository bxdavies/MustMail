using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace MustMail.App.Components.Pages.Admin;
public class AdminNavigationBase : ComponentBase
{
    [Inject] public NavigationManager Navigation { get; set; } = null!;
    protected Breakpoint Breakpoint;
    protected Task OnBreakpointChanged(Breakpoint breakpoint)
    {
        Breakpoint = breakpoint;
        StateHasChanged();
        return Task.CompletedTask;
    }

    protected bool IsCurrentPage(string page)
    {
        var currentPath = Navigation.ToBaseRelativePath(Navigation.Uri);

        return currentPath.Equals(
            $"admin/config/{page}",
            StringComparison.OrdinalIgnoreCase);
    }

    protected string CurrentConfigRoute()
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri);

        return path.StartsWith("admin/config", StringComparison.OrdinalIgnoreCase)
            ? $"/{path}"
            : string.Empty;
    } 
}