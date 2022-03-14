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
        var card = await GetCardAsync(id, flip, cancel);
        if (card == default)
        {
            return NotFound();
        }

        MergeExcessAmounts(card);

        Card = card;

        CardAlts = await CardAlternatives // unbounded: keep eye on
            .Invoke(_dbContext, card.Id, card.Name)
            .ToListAsync(cancel);

        if (Url.IsLocalUrl(returnUrl))
        {
            ReturnUrl = returnUrl;
        }

        return Page();
    }


    private Task<Card?> GetCardAsync(string cardId, bool flip, CancellationToken cancel)
    {
        return flip
            ? CardWithFlipAsync.Invoke(_dbContext, cardId, cancel)
            : CardWithoutFlipAsync.Invoke(_dbContext, cardId, cancel);
    }


    private static readonly Func<CardDbContext, string, CancellationToken, Task<Card?>> CardWithoutFlipAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, CancellationToken _) =>
            dbContext.Cards
                .Where(c => c.Id == cardId)

                .Include(c => c.Amounts
                    .OrderBy(a => a.Location.Name))
                    .ThenInclude(a => a.Location)

                .OrderBy(c => c.Id)
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefault());


    private static readonly Func<CardDbContext, string, CancellationToken, Task<Card?>> CardWithFlipAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, CancellationToken _) =>
            dbContext.Cards
                .Where(c => c.Id == cardId)

                .Include(c => c.Flip)
                .Include(c => c.Amounts
                    .OrderBy(a => a.Location.Name))
                    .ThenInclude(a => a.Location)

                .OrderBy(c => c.Id)
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefault());


    private static readonly Func<CardDbContext, string, string, IAsyncEnumerable<CardAlt>> CardAlternatives

        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, string cardName) =>
            dbContext.Cards
                .Where(c => c.Id != cardId && c.Name == cardName)
                .OrderBy(c => c.SetName)
                .Select(c => new CardAlt(c.Id, c.Name, c.SetName)));


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
            Copies = 0
        };

        foreach (var excess in excessAmounts)
        {
            mergedExcess.Copies += excess.Copies;
        }

        var mergedAmounts = card.Amounts
            .Except(excessAmounts)
            .Prepend(mergedExcess)
            .ToList();

        card.Amounts.Clear();
        card.Amounts.AddRange(mergedAmounts);
    }
}