using System;
using System.Collections.Generic;
using System.Collections.Paging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;


public class IndexModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;

    public IndexModel(PageSizes pageSizes, CardDbContext dbContext)
    {
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
        _dbContext = dbContext;
    }

    public IReadOnlyList<Bin> Bins { get; private set; } = Array.Empty<Bin>();

    public Seek<Box> Seek { get; private set; }

    public bool HasExcess { get; private set; }

    public bool HasUnclaimed { get; private set; }


    public async Task OnGetAsync(int? seek, bool backTrack, CancellationToken cancel)
    {
        var boxes = await GetBoxesAsync(seek, backTrack, cancel);
        
        Bins = boxes
            .GroupBy(b => b.Bin, (bin, _) => bin)
            .ToList();

        Seek = boxes.Seek;

        HasExcess = await _dbContext.Amounts
            .AnyAsync(a => 
                a.Location is Box && (a.Location as Box)!.IsExcess, cancel);

        HasUnclaimed = await _dbContext.Unclaimed.AnyAsync(cancel);
    }


    private IQueryable<Box> BoxesForViewing()
    {
        return _dbContext.Boxes
            .Where(b => !b.IsExcess)
            .Include(b => b.Bin)

            .Include(b => b.Cards)
                .ThenInclude(a => a.Card)

            .Include(b => b.Cards // unbounded: keep eye on
                .Where(a => a.NumCopies > 0)
                .OrderBy(a => a.Card.Name)
                    .ThenBy(a => a.Card.SetName)
                    .ThenBy(a => a.NumCopies))

            .OrderBy(b => b.Bin.Id)
                .ThenBy(b => b.Id)

            .AsNoTrackingWithIdentityResolution();
    }


    private async Task<SeekList<Box>> GetBoxesAsync(
        int? seek,
        bool backTrack,
        CancellationToken cancel)
    {
        var viewBoxes = BoxesForViewing();

        if (seek is null)
        {
            return await viewBoxes
                .ToSeekListAsync(SeekPosition.Start, _pageSize, cancel);
        }

        var box = await _dbContext.Boxes
            .OrderBy(b => b.Id)
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == seek, cancel);

        if (box == default)
        {
            return await viewBoxes
                .ToSeekListAsync(SeekPosition.Start, _pageSize, cancel);
        }

        return backTrack

            ? await viewBoxes
                .ToSeekListAsync(b =>
                    b.BinId == box.BinId && b.Id < box.Id
                        || b.BinId < box.BinId,

                    SeekPosition.End, _pageSize, cancel)

            : await viewBoxes
                .ToSeekListAsync(b =>
                    b.BinId == box.BinId && b.Id > box.Id
                        || b.BinId > box.BinId,

                    SeekPosition.Start, _pageSize, cancel);
    }
}
