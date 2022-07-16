using System;
using System.Linq;
using System.Reflection;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace MtgViewer.Services;

public sealed class PageSize : IDisposable
{
    private readonly IConfigurationSection _sizes;
    private readonly IActionContextAccessor _actionAccessor;
    private readonly RouteDataAccessor _routeAccessor;

    private int? _current;

    public PageSize(
        IConfiguration config,
        IActionContextAccessor actionAccessor,
        RouteDataAccessor routeAccessor)
    {
        _sizes = config.GetSection(nameof(PageSize));
        _actionAccessor = actionAccessor;

        _routeAccessor = routeAccessor;
        _routeAccessor.RouteChanged += ResetPage;

        Default = GetNormalizedSize(nameof(Default), 10);
        Limit = GetNormalizedSize(nameof(Limit), 256);
    }

    void IDisposable.Dispose() => _routeAccessor.RouteChanged -= ResetPage;

    public int Default { get; }
    public int Limit { get; }
    public int Current => _current ??= GetCurrentSize();

    private void ResetPage(object? sender, RouteDataEventArgs args) => _current = null;

    private int GetNormalizedSize(string key, int fallback)
    {
        return _sizes.GetValue(key, null as int?) switch
        {
            int i and > 0 => i,
            _ => fallback
        };
    }

    private int? GetNormalizedSize(string key)
    {
        return _sizes.GetValue(key, null as int?) switch
        {
            int i and > 0 => i,
            _ => null
        };
    }

    private int GetCurrentSize()
    {
        return GetComponentSize()
            ?? GetActionSize()
            ?? throw new InvalidOperationException("No action handler exists");
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

        // keep eye on, this parse does not handle all possible route templates

        string key = attribute.Template
            .Split('/')
            .Skip(1)
            .Where(s => !s.StartsWith('{'))
            .Join(':');

        if (key == string.Empty)
        {
            return GetNormalizedSize("Index", Default);
        }

        return GetNormalizedSize(key)
            ?? GetNormalizedSize($"{key}:Index", Default);
    }

    private int? GetActionSize()
    {
        string? actionName = _actionAccessor.ActionContext?.ActionDescriptor?.DisplayName;

        if (actionName is null or { Length: 0 })
        {
            return null;
        }

        string key = actionName
            .Split('/')
            .Skip(1)
            .Join(':');

        if (key == string.Empty)
        {
            return GetNormalizedSize("Index", Default);
        }

        return GetNormalizedSize(key)
            ?? GetNormalizedSize($"{key}:Index", Default);
    }
}
