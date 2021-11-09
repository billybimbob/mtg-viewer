using System.Linq;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace MTGViewer.Services;

public class PageSizes
{
    private static readonly string[] PagesNamespace = new[] 
    {
        nameof(MTGViewer), nameof(MTGViewer.Pages)
    };


    private readonly IConfigurationSection _config;

    public PageSizes(IConfiguration config)
    {
        _config = config.GetSection(nameof(PageSizes));

        Default = _config.GetValue(nameof(Default), 10);
        Limit = _config.GetValue(nameof(Limit), 256);
    }


    public int Default { get; }
    public int Limit { get; }


    public int GetSize<TPage>() where TPage : PageModel
    {
        var route = typeof(TPage).FullName
            .Split('.')
            .Except(PagesNamespace)
            .ToArray();

        var sectionKey = string.Join(':', route.SkipLast(1));
        var pageName = route.Last().Replace("Model", "");

        return _config
            .GetSection(sectionKey)
            .GetValue(pageName, Default);
        }
}