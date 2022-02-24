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


    public record CardAlt(string Id, string Name, string SetName);


    public Card Card { get; private set; } = default!;

    public IReadOnlyList<CardAlt> CardAlts { get; private set; } = Array.Empty<CardAlt>();

    public string? ReturnUrl { get; private set; }


    public async Task<IActionResult> OnGetAsync(string? id, string? returnUrl, CancellationToken cancel)
    {
        if (id is null)
        {
            return NotFound();
        }

        var card = await _dbContext.Cards
            .Include(c => c.Amounts
                .OrderBy(a => a.Location.Name))
                .ThenInclude(a => a.Location)
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
            .Select(c => new CardAlt(c.Id, c.Name, c.SetName))
            .ToListAsync(cancel);

        if (Url.IsLocalUrl(returnUrl))
        {
            ReturnUrl = returnUrl;
        }

        return Page();
    }


    private void MergeExcessAmounts(Card card)
    {
        var excessAmounts = card.Amounts
            .Where(a => a.Location is Box box && box.IsExcess);

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