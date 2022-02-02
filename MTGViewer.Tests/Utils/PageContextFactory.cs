using System;
using System.Threading.Tasks;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using Microsoft.AspNetCore.Identity;

using Moq;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Tests.Utils;

public class PageContextFactory
{
    private readonly UserManager<CardUser> _userManager;
    private readonly IUserClaimsPrincipalFactory<CardUser> _claimsFactory;

    public PageContextFactory(
        UserManager<CardUser> userManager, IUserClaimsPrincipalFactory<CardUser> claimsFactory)
    {
        _userManager = userManager;
        _claimsFactory = claimsFactory;
    }


    public void AddModelContext(PageModel model, ClaimsPrincipal? user = null)
    {
        var objectValidate = new Mock<IObjectModelValidator>();
        var requestServices = new Mock<IServiceProvider>();

        objectValidate
            .Setup(o => o.Validate(
                It.IsAny<ActionContext>(),
                It.IsAny<ValidationStateDictionary>(),
                It.IsAny<string>(),
                It.IsAny<object>() ));

        requestServices
            .Setup(p => p.GetService(
                It.Is<Type>(t => t == typeof(IObjectModelValidator)) ))
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

        var actionContext = new ActionContext(
            httpContext, 
            new RouteData(), 
            new PageActionDescriptor(), 
            modelState);

        var modelMetadataProvider = new EmptyModelMetadataProvider();
        var viewData = new ViewDataDictionary(modelMetadataProvider, modelState);

        var pageContext = new PageContext(actionContext)
        {
            ViewData = viewData
        };

        model.PageContext = pageContext;
        model.Url = new UrlHelper(actionContext);
    }


    public async Task AddModelContextAsync(PageModel model, CardUser user)
    {
        var userClaim = await _claimsFactory.CreateAsync(user);

        AddModelContext(model, userClaim);
    }


    public async Task AddModelContextAsync(PageModel model, string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);

        await AddModelContextAsync(model, user);
    }
}