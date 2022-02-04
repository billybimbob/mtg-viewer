using System.Collections.Paging;
using System.Linq;
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


    public OffsetList<Card> Cards { get; private set; } = OffsetList<Card>.Empty();

    public bool HasExcess => Cards.Offset.Total > 0;


    public async Task<IActionResult> OnGetAsync(string? cardId, int pageIndex, CancellationToken cancel)
    {
        if (await GetExcessPageAsync(cardId, cancel) is int cardPage)
        {
            return RedirectToPage(new { pageIndex = cardPage });
        }

        Cards = await ExcessCards()
            .ToOffsetListAsync(_pageSize, pageIndex, cancel);

        return Page();
    }


    private async Task<int?> GetExcessPageAsync(string? cardId, CancellationToken cancel)
    {
        if (cardId is null)
        {
            return null;
        }

        var cardName = await ExcessCards()
            .Where(c => c.Id == cardId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancel);

        if (cardName is null)
        {
            return null;
        }

        int position = await ExcessCards()
            .CountAsync(c => c.Name.CompareTo(cardName) < 0, cancel);

        // var boundary = await ExcessCards()
        //     .Select(c => new { c.Id, c.Name })
        //     .AsAsyncEnumerable()

        //     .Select((idn, Index) => (idn.Id, idn.Name, Index))
        //     .Where(ini => ini.Index % _pageSize == 0
        //         && ini.Name.CompareTo(cardName) <= 0)

        //     .OrderByDescending(ini => ini.Name.CompareTo(cardName))
        //     .Select(ini => ini.Id)
        //     .FirstOrDefaultAsync(cancel);

        return position / _pageSize;
    }


    private IQueryable<Card> ExcessCards()
    {
        return _dbContext.Cards
            .Where(c => c.Amounts
                .Any(a => a.Location is Box
                    && (a.Location as Box)!.IsExcess))

            .Include(c => c.Amounts // unbounded, keep eye on
                .Where(a => a.Location is Box 
                    && (a.Location as Box)!.IsExcess))
            
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .AsNoTrackingWithIdentityResolution();
    }
}