using System;
using System.Linq;
using System.Reflection;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace MTGViewer.Services;

public sealed class PageSize : IDisposable
{
    private readonly IConfiguration _config;
    private readonly IActionContextAccessor _actionAccessor;
    private readonly RouteDataAccessor _routeAccessor;

    public PageSize(
        IConfiguration config,
        IActionContextAccessor actionAccessor,
        RouteDataAccessor routeAccessor)
    {
        _config = config.GetSection(nameof(PageSize));
        _actionAccessor = actionAccessor;

        _routeAccessor = routeAccessor;
        _routeAccessor.RouteChanged += ResetPage;

        Default = _config.GetValue(nameof(Default), 10);
        Limit = _config.GetValue(nameof(Limit), 256);
    }

    public int Default { get; }
    public int Limit { get; }

    private int? _current;
    public int Current => _current ??= GetComponentSize() ?? GetActionSize();

    public void Dispose()
    {
        _routeAccessor.RouteChanged -= ResetPage;
    }

    private void ResetPage(object? sender, RouteDataEventArgs args)
    {
        _current = null;
    }

    private int? GetComponentSize()
    {
        if (_routeAccessor.RouteData is not RouteData data)
        {
            return null;
        }

        var attribute = data.PageType.GetCustomAttribute<RouteAttribute>(inherit: false);

        if (attribute is null)
        {
            return null;
        }

        var route = attribute.Template
            .Split('/')
            .Skip(1)
            .Where(s => !s.StartsWith('{'));

        string key = string.Join(':', route);

        if (string.IsNullOrEmpty(key))
        {
            return _config.GetValue("Index", Default);
        }

        if (_config.GetValue(key, null as int?) is int size)
        {
            return size;
        }

        return _config.GetValue($"{key}:Index", null as int?);
    }

    private int GetActionSize()
    {
        string? actionName = _actionAccessor.ActionContext?.ActionDescriptor?.DisplayName;

        if (actionName is { Length: 0 })
        {
            return Default;
        }

        string[] route = actionName?.Split('/')[1..] ?? Array.Empty<string>();

        string key = string.Join(':', route);

        if (_config.GetValue(key, null as int?) is int size)
        {
            return size;
        }

        return _config.GetValue($"{key}:Index", Default);
    }
}
