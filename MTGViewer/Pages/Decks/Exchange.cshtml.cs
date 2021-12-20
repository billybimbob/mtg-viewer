using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    private readonly ITreasuryQuery _treasuryQuery;
    private readonly UserManager<CardUser> _userManager;

    private readonly CardText _cardText;
    private readonly ILogger<ExchangeModel> _logger;

    public ExchangeModel(
        CardDbContext dbContext,
        ITreasuryQuery treasuryQuery,
        UserManager<CardUser> userManager,
        CardText cardText,
        ILogger<ExchangeModel> logger)
    {
        _dbContext = dbContext;
        _treasuryQuery = treasuryQuery;
        _userManager = userManager;

        _cardText = cardText;
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public Deck Deck { get; private set; } = null!;
    
    public bool HasPendings { get; private set; }


    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var deck = await DeckForExchange(id)
            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync(cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.TradesTo.Any())
        {
            return RedirectToPage("Index");
        }

        Deck = deck;

        HasPendings = deck.GiveBacks.Any() || await AnyWantsAsync(deck, cancel);

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


    private Task<bool> AnyWantsAsync(Deck deck, CancellationToken cancel)
    {
        if (!deck.Wants.Any())
        {
            return Task.FromResult(false);
        }

        var wantNames = deck.Wants
            .Select(w => w.Card.Name)
            .Distinct()
            .ToArray();

        return _treasuryQuery.Cards
            .Where(a => a.NumCopies > 0 
                && wantNames.Contains(a.Card.Name))
            .AnyAsync(cancel);
    }



    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var deck = await DeckForExchange(id)
            .Include(d => d.TradesFrom) // unbounded, keep eye on
            .SingleOrDefaultAsync(cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.TradesTo.Any())
        {
            return RedirectToPage("Index");
        }

        await ApplyChangesAsync(deck, cancel);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            if  (deck.Wants.Any() || deck.GiveBacks.Any())
            {
                PostMessage = "Successfully exchanged requests, but not all could be fullfilled";
            }
            else
            {
                PostMessage = "Successfully exchanged all card requests";
            }
        }
        catch (DbUpdateException e)
        {
            _logger.LogError($"ran into db error {e}");

            PostMessage = "Ran into issue while trying to exchange";
        }

        return RedirectToPage("History", new { id });
    }



    private record ExchangePair(Amount Target, int TreasuryId)
    {
        public Amount Target { get; } = NonNullOrThrow(Target);

        public int TreasuryId { get; } = TreasuryId;

        private static Amount NonNullOrThrow(Amount? value)
        {
            return value ?? throw new ArgumentNullException(nameof(Target));
        }
    }


    private async Task ApplyChangesAsync(Deck deck, CancellationToken cancel)
    {
        // TODO: add better fix for possible overlap of returning a card 
        // with the same name as a wanted card
        // potential fix could be to transfer returning cards
        // straight to wanted cards

        if (AnyOverlap(deck))
        {
            return;
        }

        var checkoutResult = await GetCheckoutsAsync(deck, cancel);
        var returnResult = await GetReturnsAsync(deck, cancel);

        if (!checkoutResult.Changes.Any() && !returnResult.Changes.Any())
        {
            return;
        }

        var transaction = new Transaction();

        _dbContext.Transactions.Attach(transaction);

        ApplyCheckouts(deck, transaction, checkoutResult);
        ApplyReturns(deck, transaction, returnResult);

        RemoveEmpty(deck);

        deck.UpdateColors(_cardText);
    }


    private bool AnyOverlap(Deck deck)
    {
        var wantCards = deck.Wants.Select(w => w.CardId);
        var giveCards = deck.GiveBacks.Select(g => g.CardId);

        return wantCards.Intersect(giveCards).Any();
    }


    private Task<RequestResult> GetCheckoutsAsync(Deck deck, CancellationToken cancel)
    {
        if (!deck.Wants.Any())
        {
            return Task.FromResult(RequestResult.Empty);
        }

        var wantRequests = deck.Wants
            .Select(w => new CardRequest(w.Card, w.NumCopies));

        return _treasuryQuery.FindCheckoutAsync(wantRequests, cancel);
    }


    private Task<RequestResult> GetReturnsAsync(Deck deck, CancellationToken cancel)
    {
        if (!deck.GiveBacks.Any())
        {
            return Task.FromResult(RequestResult.Empty);
        }

        var hasInvalidGives = deck.GiveBacks
            .GroupJoin(deck.Cards,
                give => give.CardId,
                amt => amt.CardId,
                (giveBack, amounts) => 
                    (giveBack, amtCopies: amounts.Sum(a => a.NumCopies)))
            .Any(ga =>
                ga.giveBack.NumCopies > ga.amtCopies);

        if (hasInvalidGives)
        {
            return Task.FromResult(RequestResult.Empty);
        }

        var returnRequests = deck.GiveBacks
            .Select(g => new CardRequest(g.Card, g.NumCopies));

        return _treasuryQuery.FindReturnAsync(returnRequests, cancel);
    }


    private void ApplyCheckouts(Deck deck, Transaction transaction, RequestResult result)
    {
        var (checkouts, oldCopies) = result;

        if (!checkouts.Any())
        {
            return;
        }

        _dbContext.AttachResult(result);

        AddMissingAmounts(deck, checkouts);

        var amountChanges = checkouts
            .IntersectBy(oldCopies.Keys, a => a.Id)
            .ToDictionary(
                a => a.Id, 
                a => oldCopies[a.Id] - a.NumCopies);

        AddCheckoutChanges(deck, transaction, checkouts, amountChanges);

        var wants = deck.Wants;

        var checkoutPairs = deck.Cards
            .Join(checkouts,
                da => da.CardId,
                ta => ta.CardId,
                (deckAmt, treAmt) => new ExchangePair(deckAmt, treAmt.Id))
            .ToList();

        ApplyExactCheckoutPair(amountChanges, wants, checkoutPairs);
        ApplyApproxCheckoutPairs(amountChanges, wants, checkoutPairs);
    }


    private void AddMissingAmounts(Deck deck, IEnumerable<Amount> checkouts)
    {
        var deckCards = deck.Cards
            .Select(a => a.CardId);

        var missingActualCards = checkouts
            .Select(a => a.Card)
            .ExceptBy(deckCards, c => c.Id);

        var newActuals = missingActualCards
            .Select(card => new Amount
            {
                Card = card,
                Location = deck,
                NumCopies = 0
            });

        _dbContext.Amounts.AttachRange(newActuals);
    }


    private void AddCheckoutChanges(
        Deck deck,
        Transaction transaction,
        IEnumerable<Amount> checkouts, 
        IReadOnlyDictionary<int, int> changeAmount)
    {
        var checkoutChanges = checkouts
            .Join( changeAmount,
                amt => amt.Id, 
                kva => kva.Key,
                (amt, kva) => new Change
                {
                    Card = amt.Card,
                    From = amt.Location,
                    To = deck,
                    Amount = kva.Value,
                    Transaction = transaction
                });

        _dbContext.Changes.AttachRange(checkoutChanges);
    }


    private void ApplyExactCheckoutPair(
        IDictionary<int, int> checkAmounts,
        IEnumerable<Want> wants, 
        IEnumerable<ExchangePair> pairs)
    {
        var checkoutPairs = wants
            .Join( pairs,
                want => want.CardId,
                pair => pair.Target.CardId,
                (want, pair) => (want, pair.Target, pair.TreasuryId));

        foreach(var (want, actual, matchId) in checkoutPairs)
        {
            if (!checkAmounts.TryGetValue(matchId, out int matchAmt))
            {
                continue;
            }

            int givenCopies = Math.Min(want.NumCopies, matchAmt);

            if (givenCopies == 0)
            {
                continue;
            }

            actual.NumCopies += givenCopies;
            want.NumCopies -= givenCopies;

            checkAmounts[matchId] = matchAmt - givenCopies;
        }
    }


    private void ApplyApproxCheckoutPairs(
        IDictionary<int, int> checkAmounts,
        IEnumerable<Want> wants, 
        IEnumerable<ExchangePair> pairs)
    {
        var wantGroups = wants
            .GroupBy(w => w.Card.Name,
                (_, wants) => new WantNameGroup(wants));

        var checkoutPairs = wantGroups
            .GroupJoin( pairs,
                group => group.Name,
                pair => pair.Target.Card.Name,
                (group, pairs) => (group, pairs));

        foreach(var (group, matches) in checkoutPairs)
        {
            using var matchIter = matches.GetEnumerator();

            while(group.NumCopies > 0 && matchIter.MoveNext())
            {
                (Amount actual, int matchId) = matchIter.Current;

                if (!checkAmounts.TryGetValue(matchId, out int matchAmt))
                {
                    continue;
                }

                int givenCopies = Math.Min(group.NumCopies, matchAmt);

                if (givenCopies == 0)
                {
                    continue;
                }

                actual.NumCopies += givenCopies;
                group.NumCopies -= givenCopies;

                checkAmounts[matchId] = matchAmt - givenCopies;
            }
        }
    }



    private void ApplyReturns(Deck deck, Transaction transaction, RequestResult result)
    {
        var (returns, dbCopies) = result;

        if (!returns.Any())
        {
            return;
        }

        _dbContext.AttachResult(result);

        var amountChanges = returns
            .ToDictionary(
                a => a.Id, 
                a => a.NumCopies - dbCopies.GetValueOrDefault(a.Id));

        AddReturnChanges(deck, transaction, returns, amountChanges);

        var giveBacks = deck.GiveBacks;

        var returnPairs = deck.Cards
            .Join( returns,
                da => da.CardId,
                ra => ra.CardId,
                (deckAmt, ret) => new ExchangePair(deckAmt, ret.Id));

        ApplyReturnPairs(amountChanges, giveBacks, returnPairs);
    }


    private void AddReturnChanges(
        Deck deck,
        Transaction transaction,
        IEnumerable<Amount> returns,
        IReadOnlyDictionary<int, int> changeAmount)
    {
        var returnChanges = returns
            .Join( changeAmount,
                amt => amt.Id,
                kva => kva.Key,
                (amt, kva) => new Change
                {
                    Card = amt.Card,
                    From = deck,
                    To = amt.Location,
                    Amount = kva.Value,
                    Transaction = transaction
                });

        _dbContext.Changes.AttachRange(returnChanges);
    }


    private void ApplyReturnPairs(
        IDictionary<int, int> amountChanges,
        IEnumerable<GiveBack> giveBacks,
        IEnumerable<ExchangePair> pairs)
    {
        var returnPairs = giveBacks
            .Join( pairs,
                give => give.CardId,
                pair => pair.Target.CardId,
                (give, pair) => 
                    (give, pair.Target, pair.TreasuryId));

        foreach ((GiveBack give, Amount target, int matchId) in returnPairs)
        {
            if (give.NumCopies > target.NumCopies)
            {
                continue;
            }

            if (!amountChanges.TryGetValue(matchId, out int matchAmt))
            {
                continue;
            }

            int returnApplied = Math.Min(give.NumCopies, matchAmt);

            if (returnApplied == 0)
            {
                continue;
            }

            give.NumCopies -= returnApplied;
            target.NumCopies -= returnApplied;

            amountChanges[matchId] = matchAmt - returnApplied;
        }
    }



    private void RemoveEmpty(Deck deck)
    {
        var emptyAmounts = deck.Cards
            .Where(a => a.NumCopies == 0)
            .ToList();

        var finishedWants = deck.Wants
            .Where(w => w.NumCopies == 0);

        var finishedGives = deck.GiveBacks
            .Where(g => g.NumCopies == 0);

        var emptyTrades = emptyAmounts
            .Join(deck.TradesFrom,
                a => a.CardId, t => t.CardId,
                (_, trade) => trade);

        _dbContext.Amounts.RemoveRange(emptyAmounts);
        _dbContext.Wants.RemoveRange(finishedWants);
        _dbContext.GiveBacks.RemoveRange(finishedGives);

        _dbContext.Trades.RemoveRange(emptyTrades);
    }
}
