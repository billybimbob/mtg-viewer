using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Pages.Unowned;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Unowned;

public class IndexTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private TestDataGenerator _testGen;

    private readonly IndexModel _indexModel;

    public IndexTests(
        CardDbContext dbContext,
        PageSizes pageSizes,
        SignInManager<CardUser> signInManager,
        UserManager<CardUser> userManager,
        ILogger<IndexModel> logger,
        TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _testGen = testGen;

        _indexModel = new(
            dbContext, pageSizes, signInManager, userManager, logger);
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    // [Fact]
    // public void TestName()
    // {
    // }
}