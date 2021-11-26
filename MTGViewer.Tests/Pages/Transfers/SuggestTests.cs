using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Identity;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Pages.Transfers;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Transfers;

public class SuggestTests : IAsyncLifetime
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly TestDataGenerator _testGen;

    private readonly SuggestModel _suggestModel;

    public SuggestTests(
        CardDbContext dbContext,
        PageSizes pageSizes,
        UserManager<CardUser> userManager,
        TestDataGenerator testGen)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _testGen = testGen;

        _suggestModel = new(pageSizes, dbContext, userManager);
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    // [Fact]
    // public void TestName()
    // {
        
    // }
}