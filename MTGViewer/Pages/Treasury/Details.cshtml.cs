using System;
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

public class DetailsModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly int _pageSize;

    public DetailsModel(CardDbContext dbContext, PageSizes pageSizes)
    {
        _dbContext = dbContext;
        _pageSize = pageSizes.GetPageModelSize<DetailsModel>();
    }


    public BoxPreview Box { get; private set; } = default!;

    public SeekList<QuantityPreview> Cards { get; private set; } = SeekList<QuantityPreview>.Empty;


    public async Task<IActionResult> OnGetAsync(
        int id, 
        int? seek,
        SeekDirection direction,
        bool backtrack,
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
            .Take(_pageSize)
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

                    TotalHolds = b.Holds.Sum(h => h.Copies)
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

        return await BoxCards(box)
            .WithSelect<Hold, QuantityPreview>()
            .Before(hold)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .LastOrDefaultAsync(cancel);
    }


    private IQueryable<QuantityPreview> BoxCards(BoxPreview box)
    {
        return _dbContext.Holds
            .Where(h => h.LocationId == box.Id)

            .OrderBy(h => h.Card.Name)
                .ThenBy(h => h.Card.SetName)
                .ThenBy(h => h.Copies)
                .ThenBy(h => h.Id)
            
            .Select(h => new QuantityPreview
            {
                Id = h.Id,
                Copies = h.Copies,

                Card = new CardPreview
                {
                    Id = h.CardId,
                    Name = h.Card.Name,
                    SetName = h.Card.SetName,
                    ManaCost = h.Card.ManaCost,

                    Rarity = h.Card.Rarity,
                    ImageUrl = h.Card.ImageUrl
                },
            });
    }


    private static readonly Func<CardDbContext, string, int, CancellationToken, Task<Hold?>> CardJumpAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, int boxId, CancellationToken _) =>
            dbContext.Holds
                .Where(h => h.Location is Box
                    && h.LocationId == boxId && h.CardId == cardId)
                .Include(h => h.Card)
                .OrderBy(h => h.Id)
                .SingleOrDefault());
}