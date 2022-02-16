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


    public SeekList<Card> Cards { get; private set; } = SeekList<Card>.Empty();

    public bool HasExcess =>
        Cards.Any()
            || Cards.Seek.Previous is not null
            || Cards.Seek.Next is not null;


    public async Task<IActionResult> OnGetAsync(
        string? seek, 
        int? index,
        bool backtrack, 
        string? cardId,
        CancellationToken cancel)
    {
        if (await GetCardJumpAsync(cardId, cancel) is (string cardJump, int cardIndex))
        {
            return RedirectToPage(new { seek = cardJump, index = cardIndex });
        }

        Cards = await ExcessCards()
            .ToSeekListAsync(index, _pageSize, seek, backtrack, cancel);

        return Page();
    }


    private async Task<SeekJump<string>> GetCardJumpAsync(string? id, CancellationToken cancel)
    {
        if (id is null)
        {
            return default;
        }

        var cardName = await ExcessCards()
            .Where(c => c.Id == id)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancel);

        if (cardName is null)
        {
            return default;
        }

        var options = await ExcessCards()
            .Where(c => c.Name.CompareTo(cardName) < 0)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .ToListAsync(cancel);

        return new SeekJump<string>(options.ElementAtOrDefault(^1), options.Count - 1);
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