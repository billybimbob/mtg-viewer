using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Treasury;

public class ExcessModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public ExcessModel(CardDbContext dbContext, PageSize pageSize)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    public SeekList<CardCopy> Cards { get; private set; } = SeekList.Empty<CardCopy>();

    public bool HasExcess => Cards is not { Count: 0, Seek.Previous: null, Seek.Next: null };

    public async Task<IActionResult> OnGetAsync(
        string? seek,
        SeekDirection direction,
        string? jump,
        CancellationToken cancel)
    {
        if (await FindCardJumpAsync(jump, cancel) is string cardJump)
        {
            return RedirectToPage(new
            {
                seek = cardJump,
                direction = SeekDirection.Forward,
                jump = null as string
            });
        }

        var cards = await SeekCardsAsync(direction, seek, cancel);

        if (!cards.Any() && cards.Seek is { Previous: null, Next: null })
        {
            return RedirectToPage("Index");
        }

        Cards = cards;

        return Page();
    }

    private async Task<SeekList<CardCopy>> SeekCardsAsync(
        SeekDirection direction,
        string? origin,
        CancellationToken cancel)
    {
        return await ExcessCards()

            .SeekBy(direction)
                .After(c => c.Id == origin)
                .ThenTake(_pageSize.Current)

            .Select(c => new CardCopy
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
            })

            .ToSeekListAsync(cancel);
    }

    private async Task<string?> FindCardJumpAsync(string? id, CancellationToken cancel)
    {
        if (id is null)
        {
            return null;
        }

        int size = _pageSize.Current;

        return await ExcessCards()
            .SeekBy(SeekDirection.Backwards)
                .After(c => c.Id == id)

            .Select(c => c.Id)
            .AsAsyncEnumerable()

            .Where((id, i) => i % size == size - 1)
            .LastOrDefaultAsync(cancel);
    }

    private IOrderedQueryable<Card> ExcessCards()
        => _dbContext.Cards
            .Where(c => c.Holds
                .Any(h => h.Location is Excess))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id);
}
