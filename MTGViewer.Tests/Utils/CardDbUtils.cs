using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MTGViewer.Data;


namespace MTGViewer.Tests.Utils
{
    public static class CardDbUtils
    {
        internal static DbContextOptions<CardDbContext> TestCardDbOptions()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            var dbBuilder = new DbContextOptionsBuilder<CardDbContext>()
                .UseInMemoryDatabase("Test Database")
                .UseInternalServiceProvider(serviceProvider);

            return dbBuilder.Options;
        }


        internal static void SetModelContext(this PageModel model)
        {
            var httpContext = new DefaultHttpContext();
            var modelState = new ModelStateDictionary();

            var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
            var modelMetadataProvider = new EmptyModelMetadataProvider();
            var viewData = new ViewDataDictionary(modelMetadataProvider, modelState);

            var pageContext = new PageContext(actionContext)
            {
                ViewData = viewData
            };

            model.PageContext = pageContext;
            model.Url = new UrlHelper(actionContext);
        }
    }
}