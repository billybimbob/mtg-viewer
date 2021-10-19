using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;
using MTGViewer.Areas.Identity.Data;


namespace MTGViewer.Tests.Utils
{
    public static class TestFactory
    {
        public static void InMemoryDatabase(IServiceProvider provider, DbContextOptionsBuilder options)
        {
            var inMemory = provider.GetRequiredService<InMemoryConnection>();

            options.UseInMemoryDatabase(inMemory.Database);
        }


        public static void SqliteInMemory(IServiceProvider provider, DbContextOptionsBuilder options)
        {
            var inMemory = provider.GetRequiredService<InMemoryConnection>();

            options.UseSqlite(inMemory.Connection);
        }


        public static UserManager<CardUser> CardUserManager(IServiceProvider services)
        {
            var userDb = services.GetRequiredService<UserDbContext>();
            var store = new UserStore<CardUser>(userDb);

            var options = new Mock<IOptions<IdentityOptions>>();
            var idOptions = new IdentityOptions();

            idOptions.Lockout.AllowedForNewUsers = false;
            options.Setup(o => o.Value).Returns(idOptions);

            var validator = new Mock<IUserValidator<CardUser>>();
            var userValidators = new List<IUserValidator<CardUser>>()
            {
                validator.Object
            };

            var pwdValidators = new List<PasswordValidator<CardUser>>()
            {
                new PasswordValidator<CardUser>()
            };

            var userManager = new UserManager<CardUser>(
                store,
                options.Object,
                new PasswordHasher<CardUser>(),
                userValidators,
                pwdValidators,
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(), 
                services,
                Mock.Of<ILogger<UserManager<CardUser>>>());

            validator
                .Setup(v => 
                    v.ValidateAsync(userManager, It.IsAny<CardUser>()))
                .Returns(Task.FromResult(IdentityResult.Success))
                .Verifiable();

            return userManager;
        }



        private static IUserClaimsPrincipalFactory<CardUser> CardClaimsFactory(UserManager<CardUser> userManager)
        {
            var options = new Mock<IOptions<IdentityOptions>>();
            var idOptions = new IdentityOptions();

            idOptions.Lockout.AllowedForNewUsers = false;
            options.Setup(o => o.Value).Returns(idOptions);

            return new UserClaimsPrincipalFactory<CardUser>(
                userManager,
                options.Object);
        }



        public static void SetModelContext(this PageModel model, ClaimsPrincipal user = null)
        {
            var httpContext = new DefaultHttpContext();

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


        public static async Task SetModelContextAsync(
            this PageModel model, 
            UserManager<CardUser> userManager,
            CardUser user)
        {
            var claimsFactory = CardClaimsFactory(userManager);
            var userClaim = await claimsFactory.CreateAsync(user);

            model.SetModelContext(userClaim);
        }


        public static async Task SetModelContextAsync(
            this PageModel model, 
            UserManager<CardUser> userManager,
            string userId)
        {
            var claimsFactory = CardClaimsFactory(userManager);
            var user = await userManager.FindByIdAsync(userId);
            var userClaim = await claimsFactory.CreateAsync(user);

            model.SetModelContext(userClaim);
        }
    }
}