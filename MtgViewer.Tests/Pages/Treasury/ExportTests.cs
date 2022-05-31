using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Pages.Treasury;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests.Pages.Treasury;

public class ExportTests : IAsyncLifetime
{
    private readonly ExportModel _exportModel;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;
    private readonly ActionHandlerFactory _pageContext;

    public ExportTests(
        ExportModel exportModel,
        CardDbContext dbContext,
        TestDataGenerator testGen,
        ActionHandlerFactory pageContext)
    {
        _exportModel = exportModel;
        _dbContext = dbContext;
        _testGen = testGen;
        _pageContext = pageContext;
    }

    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();

    [Fact]
    public async Task OnPost_NotSignedIn_NotFound()
    {
        _pageContext.AddPageContext(_exportModel);

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPost_UserData_File()
    {
        string userId = await _dbContext.Owners
            .Select(o => o.Id)
            .FirstAsync();

        await _pageContext.AddPageContextAsync(_exportModel, userId);

        _exportModel.DataScope = DataScope.User;

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<FileStreamResult>(result);
    }

    [Fact]
    public async Task OnPost_TreasuryData_File()
    {
        string userId = await _dbContext.Owners
            .Select(o => o.Id)
            .FirstAsync();

        await _pageContext.AddPageContextAsync(_exportModel, userId);

        _exportModel.DataScope = DataScope.Treasury;

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<FileStreamResult>(result);
    }

    [Fact]
    public async Task OnPost_CompleteData_File()
    {
        string userId = await _dbContext.Owners
            .Select(o => o.Id)
            .FirstAsync();

        await _pageContext.AddPageContextAsync(_exportModel, userId);

        _exportModel.DataScope = DataScope.Complete;

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<FileStreamResult>(result);
    }
}
