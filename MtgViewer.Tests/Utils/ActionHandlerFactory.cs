using System;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using Moq;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Services;

namespace MtgViewer.Tests.Utils;

public class ActionHandlerFactory
{
    private readonly IUserClaimsPrincipalFactory<CardUser> _claimsFactory;
    private readonly UserManager<CardUser> _userManager;
    private readonly ActionContextAccessor _actionAccessor;
    private readonly RouteDataAccessor _routeAccessor;

    public ActionHandlerFactory(
        IUserClaimsPrincipalFactory<CardUser> claimsFactory,
        UserManager<CardUser> userManager,
        ActionContextAccessor actionAccessor,
        RouteDataAccessor routeAccessor)
    {
        _userManager = userManager;
        _claimsFactory = claimsFactory;
        _actionAccessor = actionAccessor;
        _routeAccessor = routeAccessor;
    }

    public void AddPageContext(PageModel model, ClaimsPrincipal? user = null)
    {
        var objectValidate = new Mock<IObjectModelValidator>();
        var requestServices = new Mock<IServiceProvider>();

        objectValidate
            .Setup(o => o.Validate(
                It.IsAny<ActionContext>(),
                It.IsAny<ValidationStateDictionary>(),
                It.IsAny<string>(),
                It.IsAny<object>()));

        requestServices
            .Setup(p => p.GetService(
                It.Is<Type>(t => t == typeof(IObjectModelValidator))))
            .Returns(objectValidate.Object);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = requestServices.Object
        };

        if (user is not null)
        {
            httpContext.User = user;
        }

        var modelState = new ModelStateDictionary();

        // TODO: add display name based on page model

        var pageAction = new PageActionDescriptor();

        var actionContext = new ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            pageAction,
            modelState);

        _actionAccessor.ActionContext = actionContext;

        var modelMetadataProvider = new EmptyModelMetadataProvider();
        var viewData = new ViewDataDictionary(modelMetadataProvider, modelState);

        var pageContext = new PageContext(actionContext)
        {
            ViewData = viewData
        };

        model.PageContext = pageContext;
        model.Url = new UrlHelper(actionContext);
    }

    public async Task AddPageContextAsync(PageModel model, CardUser user)
    {
        var userClaim = await _claimsFactory.CreateAsync(user);

        AddPageContext(model, userClaim);
    }

    public async Task AddPageContextAsync(PageModel model, string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);

        await AddPageContextAsync(model, user);
    }

    public void AddRouteDataContext(IComponent component)
    {
        // just use empty params for now, they should not be needed

        var emptyParams = ImmutableDictionary<string, object>.Empty;

        var routeData = new RouteData(component.GetType(), emptyParams);

        _routeAccessor.RouteData = routeData;
    }

    public void AddRouteDataContext<TComponent>() where TComponent : IComponent
    {
        // just use empty params for now, they should not be needed

        var emptyParams = ImmutableDictionary<string, object>.Empty;

        var routeData = new RouteData(typeof(TComponent), emptyParams);

        _routeAccessor.RouteData = routeData;
    }
}
