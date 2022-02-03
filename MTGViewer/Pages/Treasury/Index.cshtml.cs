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

    public Offset Offset { get; private set; }

    public bool HasExcess { get; private set; }

    public bool HasUnclaimed { get; private set; }


    public async Task OnGetAsync(int? pageIndex, CancellationToken cancel)
    {
        var boxes = await BoxesForViewing()
            .ToOffsetListAsync(_pageSize, pageIndex, cancel);
        
        Bins = boxes
            .GroupBy(b => b.Bin, (bin, _) => bin)
            .ToList();

        Offset = boxes.Offset;

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
}
