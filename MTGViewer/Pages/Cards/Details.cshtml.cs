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


    public async Task<IActionResult> OnGetAsync(string id, bool flip, string? returnUrl, CancellationToken cancel)
    {
        var card = await CardForDetails(id, flip).SingleOrDefaultAsync(cancel);

        if (card == default)
        {
            return NotFound();
        }

        MergeExcessAmounts(card);

        Card = card;

        CardAlts = await CardAlternatives(card).ToListAsync(cancel);

        if (Url.IsLocalUrl(returnUrl))
        {
            ReturnUrl = returnUrl;
        }

        return Page();
    }


    private IQueryable<Card> CardForDetails(string cardId, bool flip)
    {
        var cards = _dbContext.Cards
            .Where(c => c.Id == cardId)
            .Include(c => c.Amounts
                .OrderBy(a => a.Location.Name))
                .ThenInclude(a => a.Location)
            .OrderBy(c => c.Id)
            .AsNoTrackingWithIdentityResolution();

        return flip ? cards.Include(c => c.Flip) : cards;
    }
    

    private IQueryable<CardAlt> CardAlternatives(Card card)
    {
        return _dbContext.Cards
            .Where(c => c.Id != card.Id && c.Name == card.Name)
            .OrderBy(c => c.SetName)
            .Select(c => new CardAlt(c.Id, c.Name, c.SetName));
    }


    private void MergeExcessAmounts(Card card)
    {
        var excessAmounts = card.Amounts
            .Where(a => a.Location is Excess);

        if (!excessAmounts.Any())
        {
            return;
        }

        var mergedExcess = new Amount
        {
            Card = card,
            Location = Excess.Create(),
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