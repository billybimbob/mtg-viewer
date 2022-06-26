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

    public SeekList<CardCopy> Cards { get; private set; } = SeekList.Empty<CardCopy>();

    public async Task<IActionResult> OnGetAsync(
        int id,
        string? seek,
        SeekDirection direction,
        string? jump,
        CancellationToken cancel)
    {
        // keep eye on, current flow can potentially lead to chained redirects

        var box = await BoxAsync.Invoke(_dbContext, id, cancel);

        if (box is null)
        {
            return NotFound();
        }

        if (await FindCardJumpAsync(jump, box, cancel) is string cardJump)
        {
            return RedirectToPage(new
            {
                seek = cardJump,
                direction = SeekDirection.Forward,
                jump = null as string
            });
        }

        var cards = await SeekCardsAsync(box, direction, seek, cancel);

        if (!cards.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                seek = null as int?,
                direction = SeekDirection.Forward,
                jump = null as string
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

    private async Task<string?> FindCardJumpAsync(string? id, BoxPreview box, CancellationToken cancel)
    {
        if (id is null)
        {
            return null;
        }

        int size = _pageSize.Current;

        return await BoxHolds(box)
            .SeekBy(SeekDirection.Backwards)
                .After(h => h.CardId == id)

            .Select(h => h.CardId)
            .AsAsyncEnumerable()

            .Where((id, i) => i % size == size - 1)
            .LastOrDefaultAsync(cancel);
    }

    private async Task<SeekList<CardCopy>> SeekCardsAsync(
        BoxPreview box,
        SeekDirection direction,
        string? origin,
        CancellationToken cancel)
    {
        return await BoxHolds(box)
            .SeekBy(direction)
                .After(h => h.CardId == origin)
                .ThenTake(_pageSize.Current)

            .Select(h => new CardCopy
            {
                Id = h.CardId,
                Name = h.Card.Name,

                ManaCost = h.Card.ManaCost,
                ManaValue = h.Card.ManaValue,

                SetName = h.Card.SetName,
                Rarity = h.Card.Rarity,
                ImageUrl = h.Card.ImageUrl,

                Held = h.Copies
            })

            .ToSeekListAsync(cancel);
    }

    private IOrderedQueryable<Hold> BoxHolds(BoxPreview box)
        => _dbContext.Holds
            .Where(h => h.LocationId == box.Id)
            .OrderBy(h => h.Card.Name)
                .ThenBy(h => h.Card.SetName)
                .ThenBy(h => h.Copies)
                .ThenBy(h => h.Id);
}
