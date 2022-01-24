using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;

public class ExcessModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;

    public ExcessModel(PageSizes pageSizes, CardDbContext dbContext)
    {
        _pageSize = pageSizes.GetPageModelSize<ExcessModel>();
        _dbContext = dbContext;
    }

    public PagedList<Card> Cards { get; private set; } = PagedList<Card>.Empty;

    public bool HasExcess => Cards.Pages.Total > 0;

    public async Task<IActionResult> OnGetAsync(int? id, int pageIndex, CancellationToken cancel)
    {
        if (await GetExcessPageAsync(id, cancel) is int boxPage)
        {
            return RedirectToPage(new { pageIndex = boxPage });
        }

        Cards = await ExcessCards()
            .ToPagedListAsync(_pageSize, pageIndex, cancel);

        return Page();
    }


    private async Task<int?> GetExcessPageAsync(int? id, CancellationToken cancel)
    {
        if (id is not int boxId)
        {
            return null;
        }

        bool exists = await _dbContext.Boxes
            .AnyAsync(b => b.Id == boxId && b.IsExcess, cancel);

        if (!exists)
        {
            return null;
        }

        int position = await _dbContext.Boxes
            .CountAsync(b => b.Id < boxId && b.IsExcess, cancel);

        return position / _pageSize;
    }


    private IQueryable<Card> ExcessCards()
    {
        return _dbContext.Cards
            .Where(c => c.Amounts
                .Any(a => a.Location is Box
                    && (a.Location as Box)!.IsExcess
                    && a.NumCopies > 0))

            .Include(c => c.Amounts // unbounded, keep eye on
                .Where(a => a.Location is Box 
                    && (a.Location as Box)!.IsExcess
                    && a.NumCopies > 0))
            
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)

            .AsNoTrackingWithIdentityResolution();
    }
}