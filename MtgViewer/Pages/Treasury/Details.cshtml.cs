using System;
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

public class DetailsModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public DetailsModel(CardDbContext dbContext, PageSize pageSize)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    public BoxPreview Box { get; private set; } = default!;

    public SeekList<QuantityCardPreview> Cards { get; private set; } = SeekList.Empty<QuantityCardPreview>();

    public async Task<IActionResult> OnGetAsync(
        int id,
        int? seek,
        SeekDirection direction,
        string? cardId,
        CancellationToken cancel)
    {
        // keep eye on, current flow can potentially lead to chained redirects

        var box = await BoxAsync.Invoke(_dbContext, id, cancel);

        if (box == default)
        {
            return NotFound();
        }

        if (await FindCardJumpAsync(cardId, box, cancel) is int cardJump)
        {
            return RedirectToPage(new
            {
                seek = cardJump,
                direction = SeekDirection.Forward,
                cardId = null as string
            });
        }

        var cards = await SeekCardsAsync(box, direction, seek, cancel);

        if (!cards.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                seek = null as int?,
                direction = SeekDirection.Forward,
                cardId = null as string
            });
        }

        Box = box;
        Cards = cards;

        return Page();
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<BoxPreview?>> BoxAsync
        = EF.CompileAsyncQuery((CardDbContext db, int id, CancellationToken _)
            => db.Boxes
                .Select(b => new BoxPreview
                {
                    Id = b.Id,
                    Name = b.Name,

                    Bin = new BinPreview
                    {
                        Id = b.BinId,
                        Name = b.Bin.Name
                    },

                    Appearance = b.Appearance,
                    Capacity = b.Capacity,

                    Held = b.Holds.Sum(h => h.Copies)
                })
                .SingleOrDefault(b => b.Id == id));

    private async Task<SeekList<QuantityCardPreview>> SeekCardsAsync(
        BoxPreview box,
        SeekDirection direction,
        int? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Holds
            .Where(h => h.LocationId == box.Id)

            .OrderBy(h => h.Card.Name)
                .ThenBy(h => h.Card.SetName)
                .ThenBy(h => h.Copies)
                .ThenBy(h => h.Id)

            .SeekBy(direction)
                .After(origin, h => h.Id)
                .ThenTake(_pageSize.Current)

            .Select(h => new QuantityCardPreview
            {
                Id = h.Id,
                Copies = h.Copies,

                Card = new CardPreview
                {
                    Id = h.CardId,
                    Name = h.Card.Name,

                    ManaCost = h.Card.ManaCost,
                    ManaValue = h.Card.ManaValue,

                    SetName = h.Card.SetName,
                    Rarity = h.Card.Rarity,
                    ImageUrl = h.Card.ImageUrl
                },
            })

            .ToSeekListAsync(cancel);
    }

    private async Task<int?> FindCardJumpAsync(string? cardId, BoxPreview box, CancellationToken cancel)
    {
        if (cardId is null)
        {
            return null;
        }

        var hold = await CardJumpAsync.Invoke(_dbContext, cardId, box.Id, cancel);

        if (hold is null)
        {
            return null;
        }

        int size = _pageSize.Current;

        return await _dbContext.Holds
            .Where(h => h.LocationId == box.Id)

            .OrderBy(h => h.Card.Name)
                .ThenBy(h => h.Card.SetName)
                .ThenBy(h => h.Copies)
                .ThenBy(h => h.Id)

            .SeekBy(SeekDirection.Backwards)
                .After(hold)

            .Select(c => c.Id)
            .AsAsyncEnumerable()

            .Where((id, i) => i % size == size - 1)
            .Select(id => id as int?)

            .LastOrDefaultAsync(cancel);
    }

    private static readonly Func<CardDbContext, string, int, CancellationToken, Task<Hold?>> CardJumpAsync
        = EF.CompileAsyncQuery((CardDbContext db, string card, int box, CancellationToken _)
            => db.Boxes
                .SelectMany(b => b.Holds)
                .Include(h => h.Card)
                .OrderBy(h => h.Id)
                .SingleOrDefault(h => h.CardId == card && h.LocationId == box));
}
