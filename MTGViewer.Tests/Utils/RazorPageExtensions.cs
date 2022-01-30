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

    private static IEnumerable<System.Type> GetPageModels()
    {
        var mtgViewerRef = typeof(MTGViewer.Program);
        var pageModel = typeof(PageModel);

        return mtgViewerRef.Assembly
            .GetExportedTypes()
            .Where(t => t.IsSubclassOf(pageModel));
    }
}