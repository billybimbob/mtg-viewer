using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;

[Authorize]
public class DeleteModel : PageModel
{
    private readonly CardDbContext _dbContext; 
    private readonly TreasuryHandler _treasuryHandler;

    public DeleteModel(CardDbContext dbContext, TreasuryHandler treasuryHandler)
    {
        _dbContext = dbContext;
        _treasuryHandler = treasuryHandler;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public Box Box { get; private set; } = null!;


    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var box = await _dbContext.Boxes
            .Include(b => b.Bin)

            // unbounded, keep eye on
            .Include(b => b.Cards
                .OrderBy(a => a.Card.Name)
                    .ThenBy(a => a.Card.SetName)
                    .ThenBy(a => a.NumCopies))
                .ThenInclude(a => a.Card)

            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync(b => !b.IsExcess && b.Id == id, cancel);

        if (box == default)
        {
            return NotFound();
        }

        Box = box;

        return Page();
    }


    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var box = await _dbContext.Boxes
            .Include(b => b.Bin)
            .Include(b => b.Cards) // unbounded, keep eye on
                .ThenInclude(a => a.Card)

            .SingleOrDefaultAsync(b => !b.IsExcess && b.Id == id, cancel);

        if (box == default)
        {
            return NotFound();
        }

        box.Capacity = 0; // move out all cards first

        await _treasuryHandler.UpdateAsync(_dbContext, box, cancel);

        _dbContext.Amounts.RemoveRange(box.Cards);
        _dbContext.Boxes.Remove(box);

        bool isSingleBin = await _dbContext.Boxes
            .AllAsync(b => b.Id == box.Id || b.BinId != box.BinId, cancel);

        if (isSingleBin)
        {
            _dbContext.Bins.Remove(box.Bin);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = $"Successfully Deleted {box.Name}";

            return RedirectToPage("Index");
        }
        catch (DbUpdateException)
        {
            return RedirectToPage();
        }
    }
}