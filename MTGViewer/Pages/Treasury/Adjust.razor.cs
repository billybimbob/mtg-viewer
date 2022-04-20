using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Utils;

namespace MTGViewer.Pages.Treasury;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public sealed partial class Adjust : ComponentBase, IDisposable
{
    [Parameter]
    public int BoxId { get; set; }

    [Inject]
    internal IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    internal PersistentComponentState ApplicationState { get; init; } = default!;

    [Inject]
    internal NavigationManager Nav { get; set; } = default!;

    [Inject]
    internal ILogger<Adjust> Logger { get; set; } = default!;

    internal bool IsLoading => _isBusy || !_isInteractive;

    internal IReadOnlyList<BinDto> Bins => _bins;

    internal BoxDto Box { get; } = new();

    internal bool IsFormReady { get; private set; }

    internal SaveResult Result { get; set; }

    private readonly CancellationTokenSource _cancel = new();

    private bool _isBusy;
    private bool _isInteractive;

    private PersistingComponentStateSubscription _persistSubscription;
    private BinDto[] _bins = Array.Empty<BinDto>();

    protected override async Task OnInitializedAsync()
    {
        _persistSubscription = ApplicationState.RegisterOnPersisting(PersistBoxData);

        _isBusy = true;

        try
        {
            if (ApplicationState.TryGetData(nameof(_bins), out BinDto[]? cachedBins))
            {
                _bins = cachedBins;
                return;
            }

            var token = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(token);

            _bins = await BinsAsync(dbContext).ToArrayAsync(token);
        }
        catch (OperationCanceledException e)
        {
            Logger.LogWarning("{Error}", e);
        }
        finally
        {
            _isBusy = false;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        _isBusy = true;

        try
        {
            IsFormReady = false;

            if (BoxId == default)
            {
                IsFormReady = true;
                return;
            }

            var box = await GetBoxAsync(_cancel.Token);

            if (box is null)
            {
                Logger.LogError("Box {BoxId} is not valid", BoxId);

                Nav.NavigateTo(
                    Nav.GetUriWithQueryParameter(nameof(BoxId), null as int?), replace: true);

                return;
            }

            Box.Id = box.Id;
            Box.Name = box.Name;

            Box.Appearance = box.Appearance;
            Box.Capacity = box.Capacity;

            Box.Bin.Id = box.Bin.Id;
            Box.Bin.Name = box.Bin.Name;

            IsFormReady = true;
        }
        catch (OperationCanceledException e)
        {
            Logger.LogWarning("{Error}", e);
        }
        catch (NavigationException e)
        {
            Logger.LogWarning("Navigation {Warning}", e);
        }
        finally
        {
            _isBusy = false;
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            _isInteractive = true;

            StateHasChanged();
        }
    }

    void IDisposable.Dispose()
    {
        _persistSubscription.Dispose();

        _cancel.Cancel();
        _cancel.Dispose();
    }

    private Task PersistBoxData()
    {
        ApplicationState.PersistAsJson(nameof(_bins), _bins);

        ApplicationState.PersistAsJson(nameof(Box), Box);

        return Task.CompletedTask;
    }

    private static readonly Func<CardDbContext, IAsyncEnumerable<BinDto>> BinsAsync =
        EF.CompileAsyncQuery((CardDbContext dbContext) =>
            dbContext.Bins
                .OrderBy(b => b.Name)
                .Select(b => new BinDto
                {
                    Id = b.Id,
                    Name = b.Name
                }));

    private async Task<BoxDto?> GetBoxAsync(CancellationToken cancel)
    {
        if (ApplicationState.TryGetData(nameof(Box), out BoxDto? cachedBox))
        {
            return cachedBox;
        }

        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        return await dbContext.Boxes
            .Where(b => b.Id == BoxId)
            .Select(b => new BoxDto
            {
                Id = b.Id,
                Name = b.Name,

                Appearance = b.Appearance,
                Capacity = b.Capacity,

                Bin = new BinDto
                {
                    Id = b.BinId,
                    Name = b.Bin.Name
                }
            })
            .SingleOrDefaultAsync(cancel);
    }

    internal void BinSelected(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out int id)
            && _bins.SingleOrDefault(b => b.Id == id) is var bin)
        {
            Box.Bin.Update(bin);
        }
    }

    internal async Task ValidBoxSubmittedAsync()
    {
        if (_isBusy || Box.Name is null || Box.Bin.Name is null)
        {
            return;
        }

        _isBusy = true;

        try
        {
            Result = SaveResult.None;

            await ApplyBoxChangesAsync(_cancel.Token);

            Result = SaveResult.Success;
        }
        catch (DbUpdateException e)
        {
            Result = SaveResult.Error;

            Logger.LogError("{Error}", e);
        }
        catch (OperationCanceledException e)
        {
            Result = SaveResult.Error;

            Logger.LogWarning("{Error}", e);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task ApplyBoxChangesAsync(CancellationToken cancel)
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        var box = await GetBoxAsync(dbContext, Box, cancel);
        if (box is null)
        {
            return;
        }

        var bin = await GetBinAsync(dbContext, Box.Bin, cancel);
        if (bin is null)
        {
            return;
        }

        var oldBin = box.Bin;
        box.Bin = bin;

        if (Box.IsEdit
            && oldBin != bin
            && !oldBin.Boxes.Any(b => b.Id != box.Id))
        {
            dbContext.Bins.Remove(oldBin);
        }

        await dbContext.UpdateBoxesAsync(cancel);

        await dbContext.SaveChangesAsync(cancel);

        // refresh bin list
        _bins = await BinsAsync(dbContext).ToArrayAsync(cancel);

        if (Box.IsEdit)
        {
            return;
        }

        Nav.NavigateTo(
            Nav.GetUriWithQueryParameter(nameof(BoxId), box.Id),
            replace: true);
    }

    private static async Task<Box?> GetBoxAsync(
        CardDbContext dbContext,
        BoxDto boxDto,
        CancellationToken cancel)
    {
        if (boxDto.Name is null)
        {
            return null;
        }

        Box? box;

        if (boxDto.IsEdit)
        {
            box = await dbContext.Boxes
                .Include(b => b.Bin)
                    .ThenInclude(bn => bn.Boxes
                        .Where(b => b.Id != boxDto.Id)
                        .OrderBy(b => b.Id)
                        .Take(1))
                .SingleOrDefaultAsync(b => b.Id == boxDto.Id, cancel);
        }
        else
        {
            box = new Box();
            dbContext.Boxes.Add(box);
        }

        if (box is null)
        {
            return null;
        }

        dbContext.Entry(box)
            .CurrentValues.SetValues(boxDto);

        return box;
    }

    private static async Task<Bin?> GetBinAsync(
        CardDbContext dbContext, BinDto binDto, CancellationToken cancel)
    {
        if (binDto.Name is null)
        {
            return null;
        }

        Bin? bin;

        if (binDto.IsEdit)
        {
            bin = await dbContext.Bins
                .SingleOrDefaultAsync(b => b.Id == binDto.Id, cancel);
        }
        else
        {
            bin = new Bin();

            dbContext.Bins.Add(bin);
        }

        if (bin is null)
        {
            return null;
        }

        dbContext.Entry(bin).CurrentValues.SetValues(binDto);

        return bin;
    }
}
