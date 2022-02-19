using System.Linq;
using System.Paging;
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


    public SeekList<Card> Cards { get; private set; } = SeekList<Card>.Empty;

    public bool HasExcess =>
        Cards.Any()
            || Cards.Seek.Previous is not null
            || Cards.Seek.Next is not null;


    public async Task<IActionResult> OnGetAsync(
        string? seek, 
        bool backtrack, 
        string? cardId,
        CancellationToken cancel)
    {
        if (await GetCardJumpAsync(cardId, cancel) is string cardJump)
        {
            return RedirectToPage(new { seek = cardJump });
        }

        Cards = await ExcessCards()
            .ToSeekListAsync(seek, _pageSize, backtrack, cancel);

        return Page();
    }


    private async Task<string?> GetCardJumpAsync(string? id, CancellationToken cancel)
    {
        if (id is null)
        {
            return default;
        }

        var excessCards = ExcessCards();

        var card = await excessCards
            .OrderBy(c => c.Id)
            .SingleOrDefaultAsync(c => c.Id == id, cancel);

        if (card is null)
        {
            return default;
        }

        return await excessCards
            .Before(card)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .LastOrDefaultAsync(cancel);
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