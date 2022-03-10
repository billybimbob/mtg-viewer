using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Internal;

namespace MTGViewer.Pages.Treasury;


[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public partial class Adjust : ComponentBase, IDisposable
{
    [Parameter]
    public int BoxId { get; set; }

    [Inject]
    protected IDbContextFactory<CardDbContext> DbFactory { get; set; } = default!;

    [Inject]
    protected ILogger<Adjust> Logger { get; set; } = default!;


    public bool IsBusy => _dbBusy;

    public IReadOnlyList<Bin> Bins => _bins;

    public BoxDto Box { get; } = new();

    public SaveResult Result { get; set; }


    private readonly CancellationTokenSource _cancel = new();

    private bool _dbBusy;

    private Bin[] _bins = Array.Empty<Bin>();



    protected override async Task OnInitializedAsync()
    {
        if (_dbBusy)
        {
            return;
        }

        _dbBusy = true;

        try
        {
            var cancelToken = _cancel.Token;

            await using var dbContext = await DbFactory.CreateDbContextAsync(cancelToken);

            _bins = await BinOptionsAsync(dbContext, cancelToken);
        }
        catch (OperationCanceledException e)
        {
            Logger.LogWarning(e.ToString());
        }
        finally
        {
            _dbBusy = false;
        }
    }


    private static Task<Bin[]> BinOptionsAsync(CardDbContext dbContext, CancellationToken cancel)
    {
        return dbContext.Bins
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToArrayAsync(cancel);
    }


    protected override async Task OnParametersSetAsync()
    {
        if (_dbBusy)
        {
            return;
        }
        
        if (BoxId == default)
        {
            return;
        }

        _dbBusy = true;

        try
        {
            var box = await GetBoxAsync(_cancel.Token);

            if (box != null)
            {
                Box.Id = box.Id;
                Box.Name = box.Name;

                Box.Appearance = box.Appearance;
                Box.Capacity = box.Capacity;

                Box.Bin.Id = box.Bin.Id;
                Box.Bin.Name = box.Bin.Name;
            }
        }
        catch (OperationCanceledException e)
        {
            Logger.LogWarning(e.ToString());
        }
        finally
        {
            _dbBusy = false;
        }
    }


    private async Task<Box?> GetBoxAsync(CancellationToken cancel)
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(cancel);

        dbContext.Bins.AttachRange(_bins);

        return await dbContext.Boxes
            .Include(b => b.Bin)
            .SingleOrDefaultAsync(b => b.Id == BoxId, cancel);
    }
    

    public void Dispose()
    {
        _cancel.Cancel();
        _cancel.Dispose();
    }


    public async Task ValidBoxSubmittedAsync()
    {
        if (_dbBusy || Box.Name is null || Box.Bin.Name is null)
        {
            return;
        }

        Result = SaveResult.None;

        _dbBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await ApplyBoxChangesAsync(dbContext, Box, _cancel.Token);

            await ResetAsync(dbContext, _cancel.Token);

            Result = SaveResult.Success;
        }
        catch (DbUpdateException e)
        {
            Result = SaveResult.Error;

            Logger.LogError(e.ToString());
        }
        catch (OperationCanceledException e)
        {
            Result = SaveResult.Error;

            Logger.LogWarning(e.ToString());
        }
        finally
        {
            _dbBusy = false;
        }
    }


    private static async Task ApplyBoxChangesAsync(
        CardDbContext dbContext, 
        BoxDto changes,
        CancellationToken cancel)
    {
        var box = await GetBoxAsync(dbContext, changes, cancel);
        if (box is null)
        {
            return;
        }

        var bin = await GetBinAsync(dbContext, changes.Bin, cancel);
        if (bin is null)
        {
            return;
        }

        var oldBin = box.Bin;
        box.Bin = bin;

        if (changes.IsEdit 
            && oldBin != bin 
            && !oldBin.Boxes.Any(b => b.Id != box.Id))
        {
            dbContext.Bins.Remove(oldBin);
        }

        await dbContext.UpdateBoxesAsync(cancel);

        await dbContext.SaveChangesAsync(cancel);
    }


    private static async Task<Box?> GetBoxAsync(
        CardDbContext dbContext, BoxDto boxDto, CancellationToken cancel)
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

        dbContext.Entry(bin)
            .CurrentValues.SetValues(binDto);

        return bin;
    }


    private async Task ResetAsync(CardDbContext dbContext, CancellationToken cancel)
    {
        if (!Box.Bin.IsEdit)
        {
            _bins = await BinOptionsAsync(dbContext, cancel);

            Box.Bin.Update(null);
        }

        if (Box.IsEdit)
        {
            return;
        }

        Box.Id = 0;
        Box.Name = null;

        Box.Appearance = null;
        Box.Capacity = 0;
    }


    public void BinSelected(ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out int id)
            && _bins.SingleOrDefault(b => b.Id == id) is var bin)
        {
            Box.Bin.Update(bin);
        }
    }
}