using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Tests.Utils;


public static class TestFactory
{
    public static void InMemoryDatabase(IServiceProvider provider, DbContextOptionsBuilder options)
    {
        var inMemory = provider.GetRequiredService<InMemoryConnection>();

        options
            .EnableSensitiveDataLogging()
            .UseInMemoryDatabase(inMemory.Database)
            .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning));
    }


    public static void SqliteInMemory(IServiceProvider provider, DbContextOptionsBuilder options)
    {
        var inMemory = provider.GetRequiredService<InMemoryConnection>();

        options
            .EnableSensitiveDataLogging()
            .UseSqlite(inMemory.Connection);
    }


    public static UserStore<CardUser> CardUserStore(IServiceProvider provider)
    {
        var userDb = provider.GetRequiredService<UserDbContext>();

        return new UserStore<CardUser>(userDb);
    }


    public static UserManager<CardUser> CardUserManager(IServiceProvider provider)
    {
        var store = provider.GetRequiredService<UserStore<CardUser>>();
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>();

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
            options,
            new PasswordHasher<CardUser>(),
            userValidators,
            pwdValidators,
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), 
            provider,
            Mock.Of<ILogger<UserManager<CardUser>>>());

        validator
            .Setup(v => 
                v.ValidateAsync(userManager, It.IsAny<CardUser>()))
            .Returns(Task.FromResult(IdentityResult.Success))
            .Verifiable();

        return userManager;
    }


    public static SignInManager<CardUser> CardSignInManager(IServiceProvider provider)
    {
        var store = provider.GetRequiredService<UserStore<CardUser>>();
        var userManager = provider.GetRequiredService<UserManager<CardUser>>();

        var claimsFactory = provider.GetRequiredService<IUserClaimsPrincipalFactory<CardUser>>();
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>();

        return new SignInManager<CardUser>(
            userManager,
            Mock.Of<IHttpContextAccessor>(),
            claimsFactory,
            options,
            Mock.Of<ILogger<SignInManager<CardUser>>>(),
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<CardUser>>());
    }
}