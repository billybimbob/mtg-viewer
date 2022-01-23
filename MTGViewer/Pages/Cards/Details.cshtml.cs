using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;

namespace MTGViewer.Pages.Cards;


public class DetailsModel : PageModel
{
    private readonly CardDbContext _dbContext;

    public DetailsModel(CardDbContext dbContext)
    {
        _dbContext = dbContext;
    }


    public Card Card { get; private set; } = null!;

    public IReadOnlyList<Card> CardAlts { get; private set; } = Array.Empty<Card>();


    public async Task<IActionResult> OnGetAsync(string? id, CancellationToken cancel)
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

        CardAlts = await _dbContext.Cards
            .Where(c => c.Id != id && c.Name == Card.Name)
            .OrderBy(c => c.SetName)
            .AsNoTrackingWithIdentityResolution()
            .ToListAsync(cancel);

        return Page();
    }
}