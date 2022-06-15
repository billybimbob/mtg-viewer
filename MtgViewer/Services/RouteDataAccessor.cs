using System;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace MtgViewer.Services;

public class RouteDataEventArgs : EventArgs
{
    public RouteData? RouteData { get; }

    public RouteDataEventArgs(RouteData? routeData)
    {
        RouteData = routeData;
    }
}

public class RouteDataAccessor
{
    private readonly ILogger<RouteDataAccessor> _logger;
    private RouteData? _routeData;

    public RouteDataAccessor(ILogger<RouteDataAccessor> logger)
    {
        _logger = logger;
    }

    public event EventHandler<RouteDataEventArgs>? RouteChanged;

    public RouteData? RouteData
    {
        get => _routeData;
        set
        {
            if (_routeData == value)
            {
                return;
            }

            _logger.LogInformation(
                "Updating blazor route to {RouteName}: {RouteValues}", value?.PageType.Name, value?.RouteValues);

            _routeData = value;

            RouteChanged?.Invoke(this, new RouteDataEventArgs(_routeData));
        }
    }
}
