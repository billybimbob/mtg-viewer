using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Treasury;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public class DeleteModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public DeleteModel(CardDbContext dbContext, PageSize pageSize)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public BoxPreview Box { get; private set; } = default!;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var box = await BoxAsync.Invoke(_dbContext, id, _pageSize.Current, cancel);

        if (box is null)
        {
            return NotFound();
        }

        Box = box;

        return Page();
    }

    private static readonly Func<CardDbContext, int, int, CancellationToken, Task<BoxPreview?>> BoxAsync
        = EF.CompileAsyncQuery((CardDbContext db, int box, int limit, CancellationToken _)
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
                    Held = b.Holds.Sum(h => h.Copies),

                    Cards = b.Holds
                        .OrderBy(h => h.Card.Name)
                            .ThenBy(h => h.Card.SetName)
                            .ThenBy(h => h.Copies)
                            .ThenBy(h => h.Id)

                        .Take(limit)
                        .Select(h => new LocationLink
                        {
                            Id = h.CardId,
                            Name = h.Card.Name,
                            SetName = h.Card.SetName,
                            ManaCost = h.Card.ManaCost,
                            Held = h.Copies
                        })
                })
                .SingleOrDefault(d => d.Id == box));

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var box = await _dbContext.Boxes
            .Include(b => b.Bin)
            .Include(b => b.Holds) // unbounded, keep eye on
                .ThenInclude(h => h.Card)
            .SingleOrDefaultAsync(b => b.Id == id, cancel);

        if (box is null)
        {
            return NotFound();
        }

        bool isSingleBin = await _dbContext.Boxes
            .AllAsync(b => b.Id == box.Id || b.BinId != box.BinId, cancel);

        _dbContext.Holds.RemoveRange(box.Holds);
        _dbContext.Boxes.Remove(box);

        if (isSingleBin)
        {
            _dbContext.Bins.Remove(box.Bin);
        }

        // removing box before ensures that box will not be a return target

        var cardReturns = box.Holds
            .Select(h => new CardRequest(h.Card, h.Copies));

        await _dbContext.AddCardsAsync(cardReturns, cancel);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = $"Successfully Deleted {box.Name}";

            return RedirectToPage("Index");
        }
        catch (DbUpdateException)
        {
            PostMessage = $"Ran into issue Deleting {box.Name}";

            return RedirectToPage();
        }
    }
}
