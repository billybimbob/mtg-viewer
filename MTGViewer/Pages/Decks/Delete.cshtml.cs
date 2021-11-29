using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Decks;

[Authorize]
public class DeleteModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly ITreasuryQuery _treasuryQuery;

    private readonly ILogger<DeleteModel> _logger;

    public DeleteModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        ITreasuryQuery treasuryQuery,
        ILogger<DeleteModel> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _treasuryQuery = treasuryQuery;
        _logger = logger;
    }


    [TempData]
    public string? PostMesssage { get; set; }

    public Deck Deck { get; private set; } = null!;

    public IReadOnlyList<QuantityNameGroup> NameGroups { get; private set; } = Array.Empty<QuantityNameGroup>();

    public IReadOnlyList<Trade> Trades { get; private set; } = Array.Empty<Trade>();


    public async Task<IActionResult> OnGetAsync(int id)
    {
        var deck = await DeckForDelete(id)
            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync();

        if (deck == default)
        {
            return NotFound();
        }

        var deckTrades = deck.TradesTo
            .Concat(deck.TradesFrom)
            .OrderBy(t => t.Card.Name)
            .ToList();

        Deck = deck;

        NameGroups = DeckNameGroup(deck).ToList();

        Trades = deckTrades;

        return Page();
    }


    private IQueryable<Deck> DeckForDelete(int deckId)
    {
        var userId = _userManager.GetUserId(User);

        return _dbContext.Decks
            .Where(d => d.Id == deckId && d.OwnerId == userId)

            .Include(d => d.Cards) // unbounded: keep eye on
                .ThenInclude(da => da.Card)

            .Include(d => d.Wants) // unbounded: keep eye on
                .ThenInclude(w => w.Card)

            .Include(d => d.GiveBacks) // unbounded: keep eye on
                .ThenInclude(g => g.Card)

            .Include(d => d.TradesTo) // unbounded: keep eye on
                .ThenInclude(t => t.Card)
            .Include(d => d.TradesTo)
                .ThenInclude(t => t.From)

            .Include(d => d.TradesFrom) // unbounded: keep eye on
                .ThenInclude(t => t.Card)
            .Include(d => d.TradesFrom)
                .ThenInclude(t => t.To)

            .AsSplitQuery();
    }


    private IEnumerable<QuantityNameGroup> DeckNameGroup(Deck deck)
    {
        var amountsByName = deck.Cards
            .ToLookup(ca => ca.Card.Name);

        var wantsByName = deck.Wants
            .ToLookup(w => w.Card.Name);

        var givesByName = deck.GiveBacks
            .ToLookup(g => g.Card.Name);

        var cardNames = amountsByName.Select(an => an.Key)
            .Union(wantsByName.Select(wn => wn.Key))
            .Union(givesByName.Select(gn => gn.Key))
            .OrderBy(cn => cn);

        return cardNames.Select(cn =>
            new QuantityNameGroup(
                amountsByName[cn], wantsByName[cn], givesByName[cn] ));
    }



    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deck = await DeckForDelete(id)
            .SingleOrDefaultAsync();

        if (deck == default)
        {
            return RedirectToPage("Index");
        }

        await ReturnCardsAsync(deck);

        try
        {
            await _dbContext.SaveChangesAsync();

            PostMesssage = $"Successfully deleted {deck.Name}";
        }
        catch (DbUpdateException)
        {
            PostMesssage = $"Ran into issue while trying to delete {deck.Name}";
        }

        return RedirectToPage("Index");
    }


    private async Task ReturnCardsAsync(Deck deck)
    {
        if (!deck.Cards.Any())
        {
            return;
        }

        var returningCards = deck.Cards
            .Select(a => new CardRequest(a.Card, a.NumCopies));

        var returns = await _treasuryQuery.FindReturnAsync(returningCards);
        var newTransaction = new Transaction();

        _dbContext.AttachResult(returns);
        _dbContext.Transactions.Add(newTransaction);

        var (returnTargets, targetCopies) = returns;

        var returnChanges = returnTargets
            .Select(a => new Change
            {
                Card = a.Card,
                To = a.Location,
                // no From since deck is being deleted
                Amount = a.NumCopies - targetCopies.GetValueOrDefault(a.Id),
                Transaction = newTransaction
            });

        _dbContext.Changes.AddRange(returnChanges);

        _dbContext.Amounts.RemoveRange(deck.Cards);
        _dbContext.Decks.Remove(deck);
    }
}
