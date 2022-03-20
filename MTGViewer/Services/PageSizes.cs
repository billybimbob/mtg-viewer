using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace MTGViewer.Services;

public class PageSizes
{
    private static readonly string[] PagesNamespace = new[]
    {
        nameof(MTGViewer), nameof(MTGViewer.Pages)
    };

    private const string Index = "Index";


    private readonly IConfigurationSection _config;

    public PageSizes(IConfiguration config)
    {
        _config = config.GetSection(nameof(PageSizes));

        Default = _config.GetValue(nameof(Default), 10);
        Limit = _config.GetValue(nameof(Limit), 256);
    }


    public int Default { get; }
    public int Limit { get; }


    public int GetPageModelSize<TPage>() where TPage : PageModel
    {
        var route = (typeof(TPage).FullName ?? string.Empty)
            .Split('.')
            .Except(PagesNamespace)
            .ToArray();

        var sectionKey = string.Join(':', route.SkipLast(1));
        var section = _config.GetSection(sectionKey);
        var pageName = route.Last().Replace("Model", "");

        return section.GetValue<int?>(pageName, null)
            ?? section.GetValue(Index, Default);
    }


    public int GetComponentSize<TComponent>() where TComponent : IComponent
    {
        var route = (typeof(TComponent).FullName ?? string.Empty)
            .Split('.')
            .Except(PagesNamespace)
            .ToArray();

        var sectionKey = string.Join(':', route.SkipLast(1));
        var section = _config.GetSection(sectionKey);
        var pageName = route.Last();

        return section.GetValue<int?>(pageName, null)
            ?? section.GetValue(Index, Default);
    }
}