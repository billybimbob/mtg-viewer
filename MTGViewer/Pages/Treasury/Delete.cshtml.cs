using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Pages.Treasury;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public class DeleteModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private int _pageSize;

    public DeleteModel(CardDbContext dbContext, PageSizes pageSizes)
    {
        _dbContext = dbContext;
        _pageSize = pageSizes.GetPageModelSize<DeleteModel>();
    }

    [TempData]
    public string? PostMessage { get; set; }

    public BoxPreview Box { get; private set; } = default!;


    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var box = await BoxToDeleteAsync.Invoke(_dbContext, id, _pageSize, cancel);

        if (box == default)
        {
            return NotFound();
        }

        Box = box;

        return Page();
    }


    private static readonly Func<CardDbContext, int, int, CancellationToken, Task<BoxPreview?>> BoxToDeleteAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int boxId, int pageSize, CancellationToken _) =>
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
                    TotalCards = b.Cards.Sum(a => a.Copies),

                    Cards = b.Cards
                        .OrderBy(a => a.Card.Name)
                            .ThenBy(a => a.Card.SetName)
                            .ThenBy(a => a.Copies)
                            .ThenBy(a => a.Id)

                        .Take(pageSize)
                        .Select(a => new StorageLink
                        {
                            Id = a.CardId,
                            Name = a.Card.Name,
                            SetName = a.Card.SetName,
                            ManaCost = a.Card.ManaCost,
                            Copies = a.Copies
                        })
                })
                .SingleOrDefault(d => d.Id == boxId));



    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var box = await _dbContext.Boxes
            .Include(b => b.Bin)
            .Include(b => b.Cards) // unbounded, keep eye on
                .ThenInclude(a => a.Card)
            .SingleOrDefaultAsync(b => b.Id == id, cancel);

        if (box == default)
        {
            return NotFound();
        }

        bool isSingleBin = await _dbContext.Boxes
            .AllAsync(b => b.Id == box.Id || b.BinId != box.BinId, cancel);

        _dbContext.Amounts.RemoveRange(box.Cards);
        _dbContext.Boxes.Remove(box); 

        if (isSingleBin)
        {
            _dbContext.Bins.Remove(box.Bin);
        }

        // removing box before ensures that box will not be a return target

        var cardReturns = box.Cards
            .Select(a => new CardRequest(a.Card, a.Copies));

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
