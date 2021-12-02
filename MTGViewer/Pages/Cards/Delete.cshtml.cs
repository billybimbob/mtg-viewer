using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;

namespace MTGViewer.Pages.Cards;


[Authorize]
public class DeleteModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<DeleteModel> _logger;

    public DeleteModel(CardDbContext dbContext, ILogger<DeleteModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public Card Card { get; private set; } = null!;


    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancel)
    {
        if (id is null)
        {
            return NotFound();
        }

        var card = await _dbContext.Cards
            .Include(c => c.Supertypes)
            .Include(c => c.Types)
            .Include(c => c.Subtypes)
            .Include(c => c.Amounts
                .OrderBy(ca => ca.Location.Name))
                .ThenInclude(ca => ca.Location)
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync(c => c.Id == id, cancel);

        if (card == default)
        {
            return NotFound();
        }

        Card = card;

        return Page();
    }


    public async Task<IActionResult> OnPostAsync(string id, CancellationToken cancel)
    {
        if (id is null)
        {
            return NotFound();
        }

        var card = await _dbContext.Cards.FindAsync(new [] { id }, cancel);

        if (card is null)
        {
            return RedirectToPage("Index");
        }

        _dbContext.Cards.Remove(card);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = $"Successfully deleted {card.Name}";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError(e.ToString());

            PostMessage = $"Ran into issue deleting {card.Name}";
        }

        return RedirectToPage("Index");
    }
}