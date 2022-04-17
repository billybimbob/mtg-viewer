using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Pages.Treasury;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Treasury;

public class ExportTests : IAsyncLifetime
{
    private readonly ExportModel _exportModel;
    private readonly CardDbContext _dbContext;
    private readonly TestDataGenerator _testGen;
    private readonly PageContextFactory _pageContext;

    public ExportTests(
        ExportModel exportModel,
        CardDbContext dbContext,
        TestDataGenerator testGen,
        PageContextFactory pageContext)
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
        _pageContext.AddModelContext(_exportModel);

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPost_UserData_File()
    {
        var userId = await _dbContext.Users.Select(u => u.Id).FirstAsync();

        await _pageContext.AddModelContextAsync(_exportModel, userId);

        _exportModel.BackupType = ExportModel.DataScope.User;

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<FileStreamResult>(result);
    }

    [Fact]
    public async Task OnPost_TreasuryData_File()
    {
        var userId = await _dbContext.Users.Select(u => u.Id).FirstAsync();

        await _pageContext.AddModelContextAsync(_exportModel, userId);

        _exportModel.BackupType = ExportModel.DataScope.Treasury;

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<FileStreamResult>(result);
    }

    [Fact]
    public async Task OnPost_CompleteData_File()
    {
        var userId = await _dbContext.Users.Select(u => u.Id).FirstAsync();

        await _pageContext.AddModelContextAsync(_exportModel, userId);

        _exportModel.BackupType = ExportModel.DataScope.Complete;

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<FileStreamResult>(result);
    }
}
