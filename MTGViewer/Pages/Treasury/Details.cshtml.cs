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


    public Seek Seek { get; private set; }

    public BoxPreview Box { get; private set; } = default!;

    public SeekList<AmountPreview> Cards { get; private set; } = SeekList<AmountPreview>.Empty;


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
            .OrderBy<Amount>()
            .Take(_pageSize)
            .ToSeekListAsync(cancel);

        Box = box;
        Cards = cards;
        Seek = (Seek)cards.Seek;

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

                    TotalCards = b.Cards.Sum(a => a.Copies)
                })
                .SingleOrDefault(b => b.Id == boxId));


    private async Task<int?> GetCardJumpAsync(string? cardId, BoxPreview box, CancellationToken cancel)
    {
        if (cardId is null)
        {
            return null;
        }

        var amount = await CardJumpAsync.Invoke(_dbContext, cardId, box.Id, cancel);
        if (amount is null)
        {
            return null;
        }

        return await BoxCards(box)
            .WithSelect<Amount, AmountPreview>()
            .Before(amount)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .LastOrDefaultAsync(cancel);
    }


    private IQueryable<AmountPreview> BoxCards(BoxPreview box)
    {
        return _dbContext.Amounts
            .Where(a => a.LocationId == box.Id)

            .OrderBy(a => a.Card.Name)
                .ThenBy(a => a.Card.SetName)
                .ThenBy(a => a.Copies)
                .ThenBy(a => a.Id)
            
            .Select(a => new AmountPreview
            {
                Id = a.Id,
                Copies = a.Copies,

                Card = new CardPreview
                {
                    Id = a.CardId,
                    Name = a.Card.Name,
                    SetName = a.Card.SetName,
                    ManaCost = a.Card.ManaCost,

                    Rarity = a.Card.Rarity,
                    ImageUrl = a.Card.ImageUrl
                },
            });
    }


    private static readonly Func<CardDbContext, string, int, CancellationToken, Task<AmountPreview?>> CardJumpAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, int boxId, CancellationToken _) =>
            dbContext.Amounts
                .Where(a => a.Location is Box && a.LocationId == boxId && a.CardId == cardId)
                .OrderBy(a => a.Id)

                .Select(a => new AmountPreview
                {
                    Id = a.Id,
                    Copies = a.Copies,

                    Card = new CardPreview
                    {
                        Id = a.CardId,
                        Name = a.Card.Name,
                        SetName = a.Card.SetName,
                        ManaCost = a.Card.ManaCost,

                        Rarity = a.Card.Rarity,
                        ImageUrl = a.Card.ImageUrl
                    }
                })
                .SingleOrDefault());
}