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
                    && requestNames.Contains(ba.Card.Name));
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
                await _dbContext.SaveChangesAsync();
                await _sharedStorage.ReturnAsync(boxReturns);
            }
            catch (DbUpdateException e)
            {
                _logger.LogError($"ran into db error {e}");
            }

            return RedirectToPage("./Index");
        }


        private void ApplyTakes(Deck deck, IEnumerable<BoxAmount> availables)
        {
            var takeCards = deck.Cards
                .Where(da => da.Intent is Intent.Take);

            if (!takeCards.Any() || !availables.Any())
            {
                return;
            }

            var takeGroups = takeCards
                .GroupBy(da => da.Card.Name,
                    (_, amounts) => new CardNameGroup(amounts))
                .ToDictionary(tg => tg.Name);

            var availGroups = availables
                .GroupBy(ba => ba.Card.Name,
                    (_, amounts) => new CardNameGroup(amounts))
                .ToDictionary(ag => ag.Name);

            var matches = takeGroups.Values
                .Join( availGroups.Values,
                    tg => tg.Name,
                    ag => ag.Name,
                    (tg, ag) => (tg.Name, Math.Min(tg.Amount, ag.Amount))
                );

            // TODO: prioritize taking from exact card matches
            foreach (var (name, amount) in matches)
            {
                takeGroups[name].Amount += amount;
                availGroups[name].Amount -= amount;
            }

            _dbContext.DeckAmounts.RemoveRange(takeCards.Where(da => da.Amount == 0));
        }


        private IEnumerable<(Card, int)> ApplyDeckReturns(Deck deck)
        {
            var returns = deck.Cards.Where(da => da.Intent is Intent.Return);
            var actuals = deck.Cards.Where(da => !da.HasIntent);

            if (!returns.Any())
            {
                return Enumerable.Empty<(Card, int)>();
            }

            var returnPairs = returns
                .GroupJoin( actuals,
                    ret => ret.CardId,
                    act => act.CardId,
                    (Return, Actuals) => (Return, Actuals))
                .SelectMany(
                    ras => ras.Actuals.DefaultIfEmpty(),
                    (ras, Actual) => (ras.Return, Actual))
                .ToList();

            if (returnPairs.Any(ra => ra.Actual == default))
            {
                return Enumerable.Empty<(Card, int)>();
            }

            bool hasDuplicates = returnPairs
                .GroupBy(ra => ra.Return.Id, (_, ras) => ras.Count())
                .Any(cnt => cnt > 1);

            if (hasDuplicates)
            {
                return Enumerable.Empty<(Card, int)>();
            }

            var cappedReturns = returnPairs
                .Select(ra =>
                    (ra.Actual, Change: Math.Min(ra.Actual.Amount, ra.Return.Amount)))
                .ToList();

            foreach (var (actual, change) in cappedReturns)
            {
                actual.Amount -= change;
            }

            _dbContext.DeckAmounts.RemoveRange(returns);
            _dbContext.DeckAmounts.RemoveRange(actuals.Where(da => da.Amount == 0));

            return cappedReturns
                .Select(ac => (ac.Actual.Card, ac.Change))
                .ToList();
        }
    }
}