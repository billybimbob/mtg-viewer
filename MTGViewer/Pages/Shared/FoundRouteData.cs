using Microsoft.AspNetCore.Components;

using MTGViewer.Services;

namespace MTGViewer.Pages.Shared;

public class FoundRouteData : ComponentBase
{
    [Parameter]
    public RouteData? RouteData { get; set; }

    [Inject]
    internal RouteDataAccessor RouteDataAccessor { get; set; } = default!;

    protected override void OnParametersSet()
        => RouteDataAccessor.RouteData = RouteData;
}
