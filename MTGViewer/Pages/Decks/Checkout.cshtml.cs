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


namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class CheckoutModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly ISharedStorage _sharedStorage;
        private readonly UserManager<CardUser> _userManager;
        private readonly ILogger<CheckoutModel> _logger;

        public CheckoutModel(
            CardDbContext dbContext,
            ISharedStorage sharedStorage,
            UserManager<CardUser> userManager,
            ILogger<CheckoutModel> logger)
        {
            _dbContext = dbContext;
            _sharedStorage = sharedStorage;
            _userManager = userManager;
            _logger = logger;
        }


        public Deck Deck { get; private set; }
        
        public bool HasPendings { get; private set; }

        public IReadOnlyList<RequestNameGroup> CardGroups { get; private set; }


        public async Task<IActionResult> OnGetAsync(int id)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await DeckWithCardsAndRequests(userId, id)
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var hasReturns = deck.ExchangesFrom.Any(ex => !ex.IsTrade);

            var cardNameGroups = deck.Cards
                .GroupBy(ca => ca.Card.Name,
                    (name, amounts) => (name, amounts));

            var deckRequests = deck.ExchangesTo.Concat(deck.ExchangesFrom);


            Deck = deck;

            HasPendings = hasReturns || await AvailablesForTake(deck).AnyAsync();

            CardGroups = cardNameGroups
                .GroupJoin( deckRequests,
                    nas => nas.name,
                    ex => ex.Card.Name,
                    (nas, exchanges) =>
                        new RequestNameGroup(nas.amounts, exchanges))
                .ToList();

            return Page();
        }


        private IQueryable<Deck> DeckWithCardsAndRequests(string userId, int deckId)
        {
            var userDeck = _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId);

            var withCards = userDeck
                .Include(d => d.Cards
                    .OrderBy(da => da.Card.Name))
                    .ThenInclude(ca => ca.Card);

            var withTakes = withCards
                .Include(d => d.ExchangesTo
                    .Where(ex => !ex.IsTrade)
                    .OrderBy(ex => ex.Card.Name))
                    .ThenInclude(ex => ex.Card);

            var withReturns = withTakes
                .Include(d => d.ExchangesFrom
                    .Where(ex => !ex.IsTrade)
                    .OrderBy(ex => ex.Card.Name))
                    .ThenInclude(ex => ex.Card);

            return withReturns.AsSplitQuery();
        }


        private IQueryable<CardAmount> AvailablesForTake(Deck deck)
        {
            var requestNames = deck.ExchangesTo
                .Where(da => !da.IsTrade)
                .Select(da => da.Card.Name)
                .Distinct()
                .ToArray();

            return _dbContext.Amounts
                .Where(ba => ba.Amount > 0
                    && requestNames.Contains(ba.Card.Name))
                .Include(ba => ba.Card);
        }



        public async Task<IActionResult> OnPostAsync(int id)
        {
            var userId = _userManager.GetUserId(User);
            var deck = await DeckWithCardsAndRequests(userId, id).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var availables = await AvailablesForTake(deck).ToListAsync();

            ApplyTakes(deck, availables);

            var boxReturns = ApplyDeckReturns(deck);

            try
            {
                await _sharedStorage.ReturnAsync(boxReturns);
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                _logger.LogError($"ran into db error {e}");
            }

            return Page();
        }


        private void ApplyTakes(Deck deck, IEnumerable<CardAmount> availables)
        {
            var takeCards = deck.ExchangesTo.Where(ex => ex.IsTrade);

            if (!takeCards.Any() || !availables.Any())
            {
                return;
            }

            AddMatchedActuals(deck, availables);

            var takeGroups = ToExchangeNameGroups(takeCards);
            var availGroups = ToCardNameGroups(availables);
            var actualGroups = ToCardNameGroups(deck.Cards);

            var matches = takeGroups.Values
                .Join( availGroups.Values,
                    take => take.Name,
                    avail => avail.Name,
                    (take, avail) =>
                        (take.Name, Math.Min(take.Amount, avail.Amount)) );

            // TODO: prioritize taking from exact card matches
            foreach (var (name, amount) in matches)
            {
                actualGroups[name].Amount += amount;
                availGroups[name].Amount -= amount;
                takeGroups[name].Amount -= amount;
            }

            // do not remove empty availables
            var emptyAmounts = deck.Cards.Where(da => da.Amount == 0);

            var finishedRequests = deck.ExchangesTo
                .Concat(deck.ExchangesFrom)
                .Where(ex => ex.Amount == 0);

            _dbContext.Amounts.RemoveRange(emptyAmounts);
            _dbContext.Exchanges.RemoveRange(finishedRequests);
        }


        private void AddMatchedActuals(Deck deck, IEnumerable<CardAmount> availables)
        {
            var missingActualCards = availables
                .GroupJoin( deck.Cards,
                    available => available.CardId,
                    actual => actual.CardId,
                    (available, actuals) => (available, actuals))
                .Where(aas => !aas.actuals.Any())
                .Select(aa => aa.available.Card);

            var newActuals = missingActualCards
                .Select(card => new CardAmount
                {
                    Card = card,
                    Location = deck,
                    Amount = 0
                });

            _dbContext.Amounts.AddRange(newActuals);
        }


        private IReadOnlyDictionary<string, CardNameGroup> ToCardNameGroups(
            IEnumerable<CardAmount> amounts)
        {
            return amounts
                .GroupBy(ca => ca.Card.Name,
                    (_, amounts) => new CardNameGroup(amounts))
                .ToDictionary(eg => eg.Name);
        }
        

        private IReadOnlyDictionary<string, ExchangeNameGroup> ToExchangeNameGroups(
            IEnumerable<Exchange> exchanges)
        {
            return exchanges
                .GroupBy(ex => ex.Card.Name,
                    (_, exchanges) => new ExchangeNameGroup(exchanges))
                .ToDictionary(eg => eg.Name);
        }


        private IEnumerable<(Card, int)> ApplyDeckReturns(Deck deck)
        {
            var returns = deck.ExchangesFrom.Where(ex => !ex.IsTrade);

            if (!returns.Any())
            {
                return Enumerable.Empty<(Card, int)>();
            }

            var returnPairs = returns
                .GroupJoin( deck.Cards,
                    ret => ret.CardId,
                    act => act.CardId,
                    (Return, Actuals) =>
                        (Actual: Actuals.SingleOrDefault(), Return) )
                .ToList();

            if (returnPairs.Any(ar =>
                ar.Actual == default || ar.Return.Amount > ar.Actual.Amount))
            {
                return Enumerable.Empty<(Card, int)>();
            }

            foreach (var (actualCard, returnCard) in returnPairs)
            {
                actualCard.Amount -= returnCard.Amount;
            }

            var emptyAmounts = deck.Cards.Where(ca => ca.Amount == 0);
            var finishedReturns = returns.Where(ca => ca.Amount == 0);

            _dbContext.Amounts.RemoveRange(emptyAmounts);
            _dbContext.Exchanges.RemoveRange(finishedReturns);

            return returnPairs
                .Select(ar => (ar.Actual.Card, ar.Return.Amount))
                .ToList();
        }
    }
}