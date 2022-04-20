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
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public ExcessModel(CardDbContext dbContext, PageSize pageSize)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    public SeekList<LocationCopy> Cards { get; private set; } = SeekList<LocationCopy>.Empty;

    public bool HasExcess => Cards is not { Count: 0, Seek.Previous: null, Seek.Next: null };

    public async Task<IActionResult> OnGetAsync(
        string? seek,
        SeekDirection direction,
        string? cardId,
        CancellationToken cancel)
    {
        if (await GetCardJumpAsync(cardId, cancel) is string cardJump)
        {
            return RedirectToPage(new { seek = cardJump });
        }

        var cards = await ExcessCards()
            .SeekBy(seek, direction)
            .OrderBy<Card>()
            .Take(_pageSize.Current)
            .ToSeekListAsync(cancel);

        if (!cards.Any() && cards.Seek is { Previous: null, Next: null })
        {
            return RedirectToPage("Index");
        }

        Cards = cards;

        return Page();
    }

    private IQueryable<LocationCopy> ExcessCards()
    {
        return _dbContext.Cards
            .Where(c => c.Holds
                .Any(h => h.Location is Excess))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(c => new LocationCopy
            {
                Id = c.Id,
                Name = c.Name,

                ManaCost = c.ManaCost,
                ManaValue = c.ManaValue,

                SetName = c.SetName,
                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,

                Held = c.Holds
                    .Where(h => h.Location is Excess)
                    .Sum(h => h.Copies)
            });
    }

    private async Task<string?> GetCardJumpAsync(string? id, CancellationToken cancel)
    {
        if (id is null)
        {
            return null;
        }

        var card = await _dbContext.Excess
            .SelectMany(e => e.Holds, (_, h) => h.Card)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(c => c.Id == id, cancel);

        if (card is null)
        {
            return null;
        }

        int size = _pageSize.Current;

        return await ExcessCards()
            .WithSelect<Card, LocationCopy>()
            .Before(card)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % size == size - 1)
            .LastOrDefaultAsync(cancel);
    }
}
