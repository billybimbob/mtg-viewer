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

    public SeekList<QuantityCardPreview> Cards { get; private set; } = SeekList<QuantityCardPreview>.Empty;

    public async Task<IActionResult> OnGetAsync(
        int id,
        int? seek,
        SeekDirection direction,
        string? cardId,
        CancellationToken cancel)
    {
        var box = await BoxAsync.Invoke(_dbContext, id, cancel);

        if (box == default)
        {
            return NotFound();
        }

        if (await GetCardJumpAsync(cardId, box, cancel) is int cardJump)
        {
            return RedirectToPage(new { seek = cardJump });
        }

        var cards = await BoxCards(box)
            .SeekBy(seek, direction)
            .OrderBy<Hold>()
            .Take(_pageSize.Current)
            .ToSeekListAsync(cancel);

        Box = box;
        Cards = cards;

        return Page();
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<BoxPreview?>> BoxAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int boxId, CancellationToken _) =>
            dbContext.Boxes
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
                .SingleOrDefault(b => b.Id == boxId));

    private async Task<int?> GetCardJumpAsync(string? cardId, BoxPreview box, CancellationToken cancel)
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

        return await BoxCards(box)
            .WithSelect<Hold, QuantityCardPreview>()
            .Before(hold)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % size == size - 1)
            .LastOrDefaultAsync(cancel);
    }

    private static readonly Func<CardDbContext, string, int, CancellationToken, Task<Hold?>> CardJumpAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, int boxId, CancellationToken _) =>
            dbContext.Boxes
                .SelectMany(b => b.Holds)
                .Include(h => h.Card)
                .OrderBy(h => h.Id)
                .SingleOrDefault(h => h.LocationId == boxId && h.CardId == cardId));

    private IQueryable<QuantityCardPreview> BoxCards(BoxPreview box)
    {
        return _dbContext.Holds
            .Where(h => h.LocationId == box.Id)

            .OrderBy(h => h.Card.Name)
                .ThenBy(h => h.Card.SetName)
                .ThenBy(h => h.Copies)
                .ThenBy(h => h.Id)

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
            });
    }
}
