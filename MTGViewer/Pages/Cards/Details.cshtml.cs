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


    public Card Card { get; private set; } = default!;

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
                .OrderBy(a => a.Location.Name))
                .ThenInclude(a => a.Location)
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync(c => c.Id == id, cancel);

        if (card == default)
        {
            return NotFound();
        }

        MergeExcessAmounts(card);

        Card = card;

        CardAlts = await _dbContext.Cards
            .Where(c => c.Id != id && c.Name == Card.Name)
            .OrderBy(c => c.SetName)
            .AsNoTrackingWithIdentityResolution()
            .ToListAsync(cancel);

        return Page();
    }


    private void MergeExcessAmounts(Card card)
    {
        var excessAmounts = card.Amounts
            .Where(a => a.Location is Box
                && (a.Location as Box)!.IsExcess);

        if (!excessAmounts.Any())
        {
            return;
        }

        var mergedExcess = new Amount
        {
            Card = card,
            Location = Box.CreateExcess(),
            NumCopies = 0
        };

        foreach (var excess in excessAmounts)
        {
            mergedExcess.NumCopies += excess.NumCopies;
        }

        var mergedAmounts = card.Amounts
            .Except(excessAmounts)
            .Prepend(mergedExcess)
            .ToList();

        card.Amounts.Clear();
        card.Amounts.AddRange(mergedAmounts);
    }
}