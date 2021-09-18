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

            var deck = await DeckWithCards(userId, id)
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            bool hasDeletes = deck.Cards.Any(da => da.Intent is Intent.Return);

            Deck = deck;

            HasPendings = hasDeletes || await AvailablesForTake(deck).AnyAsync();

            CardGroups = deck.Cards
                .GroupBy(da => da.Card.Name,
                    (_, amounts) => new RequestNameGroup(amounts))
                .ToList();

            return Page();
        }


        private IQueryable<Deck> DeckWithCards(string userId, int deckId)
        {
            var userDeck = _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId);

            var withCardRequests = userDeck
                .Include(d => d.Cards
                    .OrderBy(da => da.Card.Name))
                    .ThenInclude(ca => ca.Card);

            return withCardRequests;
        }


        private IQueryable<BoxAmount> AvailablesForTake(Deck deck)
        {
            var requestNames = deck.Cards
                .Where(da => da.Intent is Intent.Take)
                .Select(da => da.Card.Name)
                .Distinct()
                .ToArray();

            return _dbContext.BoxAmounts
                .Where(ba => ba.Amount > 0
                    && requestNames.Contains(ba.Card.Name))
                .Include(ba => ba.Card);
        }



        public async Task<IActionResult> OnPostAsync(int id)
        {
            var userId = _userManager.GetUserId(User);
            var deck = await DeckWithCards(userId, id).SingleOrDefaultAsync();

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


        private void ApplyTakes(Deck deck, IEnumerable<BoxAmount> availables)
        {
            var takeCards = deck.Cards.Where(da => da.Intent is Intent.Take);

            if (!takeCards.Any() || !availables.Any())
            {
                return;
            }

            var actualCards = GetMatchedActuals(deck, availables);

            var takeGroups = ToCardNameGroups(takeCards);
            var availGroups = ToCardNameGroups(availables);
            var actualGroups = ToCardNameGroups(actualCards);

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

                takeGroups[name].Amount -= amount;
                availGroups[name].Amount -= amount;
            }

            // do not remove empty availables
            var emptyAmounts = actualCards
                .Concat(takeCards)
                .Where(da => da.Amount == 0);

            _dbContext.DeckAmounts.RemoveRange(emptyAmounts);
        }


        private IEnumerable<DeckAmount> GetMatchedActuals(Deck deck, IEnumerable<BoxAmount> availables)
        {
            var actualCards = deck.Cards.Where(da => da.Intent is Intent.None);

            var missingActualCards = availables
                .GroupJoin( actualCards,
                    available => available.CardId,
                    actual => actual.CardId,
                    (available, actuals) => (available, actuals))
                .Where(aas => !aas.actuals.Any())
                .Select(aa => aa.available.Card);

            var newActuals = missingActualCards
                .Select(card => new DeckAmount
                {
                    Card = card,
                    Location = deck,
                    Amount = 0,
                    Intent = Intent.None
                });

            _dbContext.DeckAmounts.AddRange(newActuals);

            // new cards included in future enumerations
            return actualCards;
        }


        private IReadOnlyDictionary<string, CardNameGroup> ToCardNameGroups(
            IEnumerable<CardAmount> amounts)
        {
            return amounts
                .GroupBy(da => da.Card.Name,
                    (_, amounts) => new CardNameGroup(amounts))
                .ToDictionary(ag => ag.Name);
        }


        private IEnumerable<(Card, int)> ApplyDeckReturns(Deck deck)
        {
            var returns = deck.Cards.Where(da => da.Intent is Intent.Return);
            var actuals = deck.Cards.Where(da => da.Intent is Intent.None);

            if (!returns.Any())
            {
                return Enumerable.Empty<(Card, int)>();
            }

            var returnPairs = returns
                .GroupJoin( actuals,
                    ret => ret.CardId,
                    act => act.CardId,
                    (Return, Actuals) =>
                        (Actual: Actuals.SingleOrDefault(), Return) )
                .ToList();

            if (returnPairs.Any(ar => ar.Actual == default || ar.Return.Amount > ar.Actual.Amount))
            {
                return Enumerable.Empty<(Card, int)>();
            }

            foreach (var (actualCard, returnCard) in returnPairs)
            {
                actualCard.Amount -= returnCard.Amount;
            }

            var emptyAmounts = actuals
                .Where(da => da.Amount == 0)
                .Concat(returns);

            _dbContext.DeckAmounts.RemoveRange(emptyAmounts);

            return returnPairs
                .Select(ar => (ar.Actual.Card, ar.Return.Amount))
                .ToList();
        }
    }
}