using Microsoft.AspNetCore.Components;

using MtgViewer.Services;

namespace MtgViewer.Pages.Shared;

public class FoundRouteData : ComponentBase
{
    [Parameter]
    public RouteData? RouteData { get; set; }

    [Inject]
    internal RouteDataAccessor RouteDataAccessor { get; set; } = default!;

    protected override void OnParametersSet()
        => RouteDataAccessor.RouteData = RouteData;
}
