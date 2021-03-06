using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Microsoft.Extensions.DependencyInjection;

public static class RazorPageExtensions
{
    public static IServiceCollection AddRazorPageModels(this IServiceCollection collection)
    {
        foreach (var pageModel in GetPageModels())
        {
            collection.AddScoped(pageModel);
        }

        return collection;
    }

    private static IEnumerable<Type> GetPageModels()
    {
        var mtgViewerRef = typeof(MtgViewer.App);
        var pageModel = typeof(PageModel);

        return mtgViewerRef.Assembly.ExportedTypes
            .Where(t => t.IsSubclassOf(pageModel));
    }
}
