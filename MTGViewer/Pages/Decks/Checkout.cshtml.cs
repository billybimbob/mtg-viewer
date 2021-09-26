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
            var deck = await DeckWithCardsAndRequests(id)
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var hasReturns = deck.ExchangesFrom.Any(ex => !ex.IsTrade);

            Deck = deck;

            HasPendings = hasReturns || await AvailablesForTake(deck).AnyAsync();

            CardGroups = DeckNameGroups(deck).ToList();

            return Page();
        }


        private IEnumerable<RequestNameGroup> DeckNameGroups(Deck deck)
        {
            var amountsByName = deck.Cards
                .ToLookup(ca => ca.Card.Name);

            var requestsByName = deck.GetAllExchanges()
                .Where(ex => !ex.IsTrade)
                .ToLookup(ex => ex.Card.Name);

            var cardNames = amountsByName
                .Select(g => g.Key)
                .Union(requestsByName.Select(g => g.Key))
                .OrderBy(cn => cn);

            return cardNames.Select(cn =>
                new RequestNameGroup(amountsByName[cn], requestsByName[cn]));
        }


        private IQueryable<Deck> DeckWithCardsAndRequests(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)

                .Include(d => d.ExchangesTo
                    .Where(ex => !ex.IsTrade))
                    .ThenInclude(ex => ex.Card)

                .Include(d => d.ExchangesFrom
                    .Where(ex => !ex.IsTrade))
                    .ThenInclude(ex => ex.Card)

                .AsSplitQuery();
        }


        private IQueryable<CardAmount> AvailablesForTake(Deck deck)
        {
            var requestNames = deck.ExchangesTo
                .Where(da => !da.IsTrade)
                .Select(da => da.Card.Name)
                .Distinct()
                .ToArray();

            return _dbContext.Amounts
                .Where(ca => ca.Location is Box
                    && ca.Amount > 0
                    && requestNames.Contains(ca.Card.Name))
                .Include(ba => ba.Card);
        }



        public async Task<IActionResult> OnPostAsync(int id)
        {
            var deck = await DeckWithCardsAndRequests(id).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var availables = await AvailablesForTake(deck).ToListAsync();

            ApplyTakes(deck, availables);

            var boxReturns = ApplyDeckReturns(deck);

            try
            {
                await _dbContext.SaveChangesAsync();
                await _sharedStorage.ReturnAsync(boxReturns);
            }
            catch (DbUpdateException e)
            {
                _logger.LogError($"ran into db error {e}");
            }

            return Page();
        }


        private void ApplyTakes(Deck deck, IEnumerable<CardAmount> availables)
        {
            var takeCards = deck.ExchangesTo.Where(ex => !ex.IsTrade);

            if (!availables.Any() || !takeCards.Any())
            {
                return;
            }

            var actuals = GetAllActuals(deck, availables);

            ApplyExactTakes(takeCards, availables, actuals);

            var remainingTakes = takeCards.Where(ex => ex.Amount > 0);
            var remainingAvails = availables.Where(ca => ca.Amount > 0);

            ApplyCloseTakes(remainingTakes, remainingAvails, actuals);

            var emptyAmounts = deck.Cards.Where(da => da.Amount == 0);
            var finishedRequests = deck.GetAllExchanges().Where(ex => ex.Amount == 0);

            // do not remove empty availables
            _dbContext.Amounts.RemoveRange(emptyAmounts);
            _dbContext.Exchanges.RemoveRange(finishedRequests);
        }


        private IReadOnlyDictionary<string, CardAmount> GetAllActuals(
            Deck deck, IEnumerable<CardAmount> availables)
        {
            var missingActualCards = availables
                .GroupJoin( deck.Cards,
                    available => available.CardId,
                    actual => actual.CardId,
                    (available, actuals) => (available, actuals))
                .Where(aas => 
                    !aas.actuals.Any())
                .Select(aa => aa.available.Card)
                .Distinct();

            var newActuals = missingActualCards
                .Select(card => new CardAmount
                {
                    Card = card,
                    Location = deck,
                    Amount = 0
                });

            _dbContext.Amounts.AddRange(newActuals);

            return deck.Cards.ToDictionary(ca => ca.CardId);
        }


        private void ApplyExactTakes(
            IEnumerable<Exchange> takes,
            IEnumerable<CardAmount> availables,
            IReadOnlyDictionary<string, CardAmount> actuals)
        {
            var exactMatches = takes
                .GroupJoin( availables,
                    take => take.CardId,
                    avail => avail.CardId,
                    (take, avails) => (take, avails))
                .Where(tas => 
                    tas.avails.Any());

            foreach (var (take, avails) in exactMatches)
            {
                using var availableToTake = avails.GetEnumerator();
                var actual = actuals[take.CardId];

                while (take.Amount > 0 && availableToTake.MoveNext())
                {
                    var currentAvail = availableToTake.Current;
                    int amountTaken = Math.Min(take.Amount, currentAvail.Amount);

                    currentAvail.Amount -= amountTaken;
                    take.Amount -= amountTaken;

                    actual.Amount += amountTaken;
                }
            }
        }


        private void ApplyCloseTakes(
            IEnumerable<Exchange> takes,
            IEnumerable<CardAmount> availables,
            IReadOnlyDictionary<string, CardAmount> actuals)
        {
            var takesByName = takes
                .GroupBy(ex => ex.Card.Name,
                    (_, takes) => new ExchangeNameGroup(takes));

            var availsByName = availables
                .GroupBy(ca => ca.Card.Name,
                    (_, avails) => new CardNameGroup(avails));

            var closeMatches = takesByName
                .Join( availsByName,
                    takeGroup => takeGroup.Name,
                    availGroup => availGroup.Name,
                    (takes, availGroup) => (takes, availGroup));


            foreach (var (takeGroup, availGroup) in closeMatches)
            {
                using var closeAvails = availGroup.GetEnumerator();

                while (takeGroup.Amount > 0 && closeAvails.MoveNext())
                {
                    var currentAvail = closeAvails.Current;
                    var actual = actuals[currentAvail.CardId];

                    int amountTaken = Math.Min(takeGroup.Amount, currentAvail.Amount);

                    takeGroup.Amount -= amountTaken;
                    currentAvail.Amount -= amountTaken;

                    actual.Amount += amountTaken;
                }
            }
        }



        private IReadOnlyList<(Card, int)> ApplyDeckReturns(Deck deck)
        {
            var returns = deck.ExchangesFrom.Where(ex => !ex.IsTrade);

            var returnPairs = deck.Cards
                .Join( returns,
                    act => act.CardId,
                    ret => ret.CardId,
                    (Actual, Return) => (Actual, Return));

            var appliedReturns = new List<Exchange>();

            foreach (var (actual, returnRequest) in returnPairs)
            {
                if (actual.Amount >= returnRequest.Amount)
                {
                    actual.Amount -= returnRequest.Amount;
                    appliedReturns.Add(returnRequest);
                }
            }

            var emptyAmounts = deck.Cards.Where(ca => ca.Amount == 0);

            _dbContext.Amounts.RemoveRange(emptyAmounts);
            _dbContext.Exchanges.RemoveRange(appliedReturns);

            return appliedReturns
                .Select(ex => (ex.Card, ex.Amount))
                .ToList();
        }
    }
}