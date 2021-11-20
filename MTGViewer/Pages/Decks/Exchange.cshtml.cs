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
public class ExchangeModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly ITreasury _treasury;
    private readonly UserManager<CardUser> _userManager;

    private readonly CardText _cardText;
    private readonly ILogger<ExchangeModel> _logger;

    public ExchangeModel(
        CardDbContext dbContext,
        ITreasury treasury,
        UserManager<CardUser> userManager,
        CardText cardText,
        ILogger<ExchangeModel> logger)
    {
        _dbContext = dbContext;
        _treasury = treasury;
        _userManager = userManager;

        _cardText = cardText;
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public Deck Deck { get; private set; } = null!;
    
    public bool HasPendings { get; private set; }


    public async Task<IActionResult> OnGetAsync(int id)
    {
        var deck = await DeckForExchange(id)
            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync();

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.TradesTo.Any())
        {
            return RedirectToPage("Index");
        }

        Deck = deck;

        HasPendings = deck.GiveBacks.Any() || await AnyWantsAsync(deck);

        return Page();
    }


    private IQueryable<Deck> DeckForExchange(int deckId)
    {
        var userId = _userManager.GetUserId(User);

        return _dbContext.Decks
            .Where(d => d.Id == deckId && d.OwnerId == userId)

            .Include(d => d.Cards // unbounded: keep eye one
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName))
                .ThenInclude(ca => ca.Card)

            .Include(d => d.Wants // unbounded: keep eye one
                .OrderBy(w => w.Card.Name)
                    .ThenBy(w => w.Card.SetName))
                .ThenInclude(w => w.Card)

            .Include(d => d.GiveBacks // unbounded: keep eye one
                .OrderBy(g => g.Card.Name)
                    .ThenBy(g => g.Card.SetName))
                .ThenInclude(g => g.Card)

            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))

            .AsSplitQuery();
    }


    private Task<bool> AnyWantsAsync(Deck deck)
    {
        if (!deck.Wants.Any())
        {
            return Task.FromResult(false);
        }

        var wantNames = deck.Wants
            .Select(w => w.Card.Name)
            .Distinct()
            .ToArray();

        return _treasury.Cards
            .Where(a => a.NumCopies > 0 
                && wantNames.Contains(a.Card.Name))
            .AnyAsync();
    }



    public async Task<IActionResult> OnPostAsync(int id)
    {
        var deck = await DeckForExchange(id).SingleOrDefaultAsync();

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.TradesTo.Any())
        {
            return RedirectToPage("Index");
        }

        try
        {
            var transaction = await _treasury.ExchangeAsync(deck);

            if (transaction is null)
            {
                PostMessage = "Ran into issue while trying to exchange";
            }
            else
            {
                ApplyChangesToDeck(transaction, deck);
                
                await _dbContext.SaveChangesAsync();

                PostMessage = deck.Wants.Any() || deck.GiveBacks.Any()
                    ? "Successfully exchanged requests, but not all could be fullfilled"
                    : "Successfully exchanged all card requests";
            }
        }
        catch (DbUpdateException e)
        {
            _logger.LogError($"ran into db error {e}");

            PostMessage = "Ran into issue while trying to exchange";
        }

        return RedirectToPage("History", new { id });
    }


    private void ApplyChangesToDeck(Transaction transaction, Deck deck)
    {
        var removeChanges = transaction.Changes
            .Where(c => c.FromId == deck.Id);

        var addChanges = transaction.Changes
            .Where(c => c.ToId == deck.Id)
            .ToHashSet();

        var allAmounts = GetAllActuals(deck, addChanges);

        ApplyGiveBacks(removeChanges, deck.GiveBacks, allAmounts);

        ApplyExactWants(addChanges, deck.Wants, allAmounts);
        ApplyApproxWants(addChanges, deck.Wants, allAmounts);

        RemoveEmpty(deck);

        deck.UpdateColors(_cardText);
    }


    private IReadOnlyDictionary<string, Amount> GetAllActuals(Deck deck, IEnumerable<Change> addChanges)
    {
        var missingActualCards = addChanges
            .ExceptBy(
                deck.Cards.Select(a => a.CardId),
                c => c.CardId)
            .Select(c => c.Card)
            .DistinctBy(c => c.Id);

        var newActuals = missingActualCards
            .Select(card => new Amount
            {
                Card = card,
                Location = deck,
                NumCopies = 0
            });

        _dbContext.Amounts.AddRange(newActuals);

        return deck.Cards.ToDictionary(a => a.CardId);
    }


    private void ApplyGiveBacks(
        IEnumerable<Change> removeChanges,
        IEnumerable<GiveBack> giveBacks, 
        IReadOnlyDictionary<string, Amount> amounts)
    {
        var returnPairs = giveBacks
            .GroupJoin( removeChanges,
                gb => gb.CardId,
                rc => rc.CardId,
                (giveBack, removes) => (giveBack, removes.Sum(c => c.Amount)));

        foreach (var (giveBack, totalReturned) in returnPairs)
        {
            var amount = amounts[giveBack.CardId];

            giveBack.NumCopies -= totalReturned;
            amount.NumCopies -= totalReturned;
        }
    }


    private void ApplyExactWants(
        ICollection<Change> addChanges,
        IEnumerable<Want> wants,
        IReadOnlyDictionary<string, Amount> amounts)
    {
        var exactMatches = wants
            .Join( addChanges,
                want => want.CardId,
                change => change.CardId,
                (want, change) => (want, change))
            .ToList();

        foreach (var (want, change) in exactMatches)
        {
            if (!addChanges.Contains(change))
            {
                continue;
            }

            int amountTaken = Math.Min(want.NumCopies, change.Amount);

            if (amountTaken == change.Amount)
            {
                addChanges.Remove(change);
            }

            if (amountTaken == 0)
            {
                continue;
            }

            var actual = amounts[want.CardId];

            actual.NumCopies += amountTaken;
            want.NumCopies -= amountTaken;
        }
    }


    private void ApplyApproxWants(
        IEnumerable<Change> addChanges,
        IEnumerable<Want> wants,
        IReadOnlyDictionary<string, Amount> amounts)
    {
        var wantsByName = wants
            .GroupBy(w => w.Card.Name,
                (_, ws) => new WantNameGroup(ws));

        var addsByName = addChanges
            .GroupBy(c => c.Card.Name,
                (name, cs) => (name, cards: cs.Select(c => (c.CardId, c.Amount))) );

        var closeMatches = wantsByName
            .Join( addsByName,
                wantGroup => wantGroup.Name,
                nameGroup => nameGroup.name,
                (wantGroup, nameGroup) => (wantGroup, nameGroup.cards));

        foreach (var (wantGroup, cards) in closeMatches)
        {
            using var matches = cards.GetEnumerator();

            while (wantGroup.NumCopies > 0 && matches.MoveNext())
            {
                var match = matches.Current;
                var actual = amounts[match.CardId];

                int amountTaken = Math.Min(wantGroup.NumCopies, match.Amount);

                wantGroup.NumCopies -= amountTaken;
                actual.NumCopies += amountTaken;
            }
        }
    }


    private void RemoveEmpty(Deck deck)
    {
        var emptyAmounts = deck.Cards.Where(a => a.NumCopies == 0);
        var finishedWants = deck.Wants.Where(w => w.NumCopies == 0);
        var finishedGives = deck.GiveBacks.Where(g => g.NumCopies == 0);

        // do not remove empty availables
        _dbContext.Amounts.RemoveRange(emptyAmounts);
        _dbContext.Wants.RemoveRange(finishedWants);
        _dbContext.GiveBacks.RemoveRange(finishedGives);
    }
}