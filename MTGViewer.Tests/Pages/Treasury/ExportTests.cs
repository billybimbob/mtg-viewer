using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Xunit;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Pages.Treasury;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests.Pages.Treasury;

public class ExportTests : IAsyncLifetime
{
    private readonly ExportModel _exportModel;
    private readonly CardDbContext _dbContext;
    private readonly BulkOperations _bulkOperations;
    private readonly TestDataGenerator _testGen;

    public ExportTests(
        ExportModel exportModel,
        CardDbContext dbContext, 
        BulkOperations bulkOperations,
        TestDataGenerator testGen)
    {
        _exportModel = exportModel;
        _dbContext = dbContext;
        _bulkOperations = bulkOperations;
        _testGen = testGen;
    }


    public Task InitializeAsync() => _testGen.SeedAsync();

    public Task DisposeAsync() => _testGen.ClearAsync();


    [Fact]
    public async Task OnPost_NullDownload_NotFound()
    {
        _exportModel.Download = null;

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<NotFoundResult>(result);
    }


    [Theory]
    [InlineData(-2)]
    [InlineData(-100)]
    [InlineData(1)]
    [InlineData(1_000)]
    public async Task OnPost_InvalidSection_NotFound(int section)
    {
        if (section > 0)
        {
            section += await _bulkOperations.GetTotalPagesAsync();
        }

        _exportModel.Download = new ExportModel.DownloadModel
        {
            Section = section
        };

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<NotFoundResult>(result);
    }


    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task OnPost_ValidSection_File(int? section)
    {
        if (section is int i)
        {
            section = Math.Min(i, await _bulkOperations.GetTotalPagesAsync());
        }

        _exportModel.Download = new ExportModel.DownloadModel
        {
            Section = section
        };

        var result = await _exportModel.OnPostAsync(default);

        Assert.IsType<FileContentResult>(result);
    }
}