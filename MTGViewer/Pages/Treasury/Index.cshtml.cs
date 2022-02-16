using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
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

    private IReadOnlyDictionary<int, int>? _boxSpace;

    public IndexModel(PageSizes pageSizes, CardDbContext dbContext)
    {
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
        _dbContext = dbContext;
    }

    public IReadOnlyList<Bin> Bins { get; private set; } = Array.Empty<Bin>();

    public Seek<Box> Seek { get; private set; }

    public bool HasExcess { get; private set; }

    public bool HasUnclaimed { get; private set; }


    public async Task OnGetAsync(int? seek, int? index, bool backtrack, CancellationToken cancel)
    {
        var boxes = await BoxesForViewing()
            .ToSeekListAsync(index, _pageSize, seek, backtrack, cancel);

        _boxSpace = await _dbContext.Boxes
            .Select(b => new { b.Id, Total = b.Cards.Sum(a => a.NumCopies) })
            .ToDictionaryAsync(
                b => b.Id, b => b.Total);

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
                .OrderBy(a => a.Card.Name)
                    .ThenBy(a => a.Card.SetName)
                    .ThenBy(a => a.NumCopies)
                .Take(_pageSize))

            .OrderBy(b => b.Bin.Id)
                .ThenBy(b => b.Id)

            .AsNoTrackingWithIdentityResolution();
    }


    public int TotalCards(Box box)
    {
        return _boxSpace is null ? 0 : _boxSpace.GetValueOrDefault(box.Id);
    }

    public bool HasMoreCards(Box box)
    {
        return box.Cards.Sum(a => a.NumCopies) < TotalCards(box);
    }
}
