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

#nullable enable

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


        [TempData]
        public string? PostMessage { get; set; }

        public Deck? Deck { get; private set; }
        
        public bool HasPendings { get; private set; }


        public async Task<IActionResult> OnGetAsync(int id)
        {
            var deck = await DeckForCheckout(id)
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

            HasPendings = deck.Wants.Any(cr => cr.IsReturn) 
                || await TakeTargets(deck).AnyAsync();

            return Page();
        }


        private IQueryable<Deck> DeckForCheckout(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.Cards
                    // unbounded: keep eye one
                    .OrderBy(ca => ca.Card.Name)
                        .ThenBy(ca => ca.Card.SetName))
                    .ThenInclude(ca => ca.Card)

                .Include(d => d.Wants
                    // unbounded: keep eye one
                    .OrderBy(cr => cr.Card.Name)
                        .ThenBy(cr => cr.Card.SetName))
                    .ThenInclude(cr => cr.Card)

                .Include(d => d.TradesTo)
                    // unbounded: limit
                .AsSplitQuery();
        }


        private IQueryable<CardAmount> TakeTargets(Deck deck)
        {
            var takeNames = deck.Wants
                .Where(cr => !cr.IsReturn)
                .Select(cr => cr.Card.Name)
                .Distinct()
                .ToArray();

            return _dbContext.Amounts
                .Where(ca => ca.Location is Box
                    && ca.Amount > 0
                    && takeNames.Contains(ca.Card.Name))
                .Include(ca => ca.Card)
                .Include(ca => ca.Location);
        }



        public async Task<IActionResult> OnPostAsync(int id)
        {
            var deck = await DeckForCheckout(id).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (deck.TradesTo.Any())
            {
                return RedirectToPage("Index");
            }

            var availables = await TakeTargets(deck).ToListAsync();
                // unbounded, keep eye on, or limit

            ApplyTakes(deck, availables);

            var boxReturns = ApplyDeckReturns(deck);

            RemoveEmpty(deck);

            try
            {
                if (boxReturns.Any())
                {
                    await _sharedStorage.ReturnAsync(boxReturns);
                }

                await _dbContext.SaveChangesAsync();

                PostMessage = "Successfully exchanged cards";
            }
            catch (DbUpdateException e)
            {
                _logger.LogError($"ran into db error {e}");
                PostMessage = "Ran into issue while trying to checkout";
            }

            return RedirectToPage("History", new { deckId = id });
        }



        private void ApplyTakes(Deck deck, IEnumerable<CardAmount> availables)
        {
            var takes = deck.Wants.Where(cr => !cr.IsReturn);

            if (!availables.Any() || !takes.Any())
            {
                return;
            }

            var transaction = new Transaction();
            var actuals = GetAllActuals(deck, availables);

            _dbContext.Transactions.Attach(transaction);

            ApplyExactTakes(transaction, takes, availables, actuals);

            var remainingTakes = takes.Where(cr => cr.Amount > 0);
            var remainingAvails = availables.Where(ca => ca.Amount > 0);

            ApplyCloseTakes(transaction, remainingTakes, remainingAvails, actuals);
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
            Transaction transaction,
            IEnumerable<CardRequest> takes,
            IEnumerable<CardAmount> availables,
            IReadOnlyDictionary<string, CardAmount> actuals)
        {
            var exactMatches = takes
                .Join( availables,
                    take => take.CardId,
                    avail => avail.CardId,
                    (take, avail) => (take, avail));

            foreach (var (take, available) in exactMatches)
            {
                var actual = actuals[take.CardId];
                int amountTaken = Math.Min(take.Amount, available.Amount);

                actual.Amount += amountTaken;
                available.Amount -= amountTaken;
                take.Amount -= amountTaken;

                var newChange = new Change
                {
                    Card = available.Card,

                    To = actual.Location,
                    From = available.Location,

                    Amount = amountTaken,
                    Transaction = transaction
                };

                _dbContext.Changes.Attach(newChange);
            }
        }


        private void ApplyCloseTakes(
            Transaction transaction,
            IEnumerable<CardRequest> takes,
            IEnumerable<CardAmount> availables,
            IReadOnlyDictionary<string, CardAmount> actuals)
        {
            var takesByName = takes
                .GroupBy(cr => cr.Card.Name,
                    (_, takes) => new RequestNameGroup(takes));

            var availsByName = availables
                .GroupBy(ca => ca.Card.Name,
                    (_, avails) => new CardNameGroup(avails));

            var closeMatches = takesByName
                .Join( availsByName,
                    takeGroup => takeGroup.Name,
                    availGroup => availGroup.Name,
                    (takeGroup, availGroup) => (takeGroup, availGroup));


            foreach (var (takeGroup, availGroup) in closeMatches)
            {
                using var closeAvails = availGroup.GetEnumerator();

                while (takeGroup.Amount > 0 && closeAvails.MoveNext())
                {
                    var currentAvail = closeAvails.Current;
                    var actual = actuals[currentAvail.CardId];

                    int amountTaken = Math.Min(takeGroup.Amount, currentAvail.Amount);

                    actual.Amount += amountTaken;

                    currentAvail.Amount -= amountTaken;
                    takeGroup.Amount -= amountTaken;

                    var newChange = new Change
                    {
                        Card = currentAvail.Card,

                        To = actual.Location,
                        From = currentAvail.Location,

                        Amount = amountTaken,
                        Transaction = transaction
                    };

                    _dbContext.Changes.Attach(newChange);
                }
            }
        }



        private IReadOnlyList<CardReturn> ApplyDeckReturns(Deck deck)
        {
            var returns = deck.Wants.Where(cr => cr.IsReturn);

            var returnPairs = deck.Cards
                .Join( returns,
                    act => act.CardId,
                    ret => ret.CardId,
                    (Actual, Return) => (Actual, Return));

            // TODO: change to atomic returns, all or nothing
            var appliedReturns = new List<CardRequest>();

            foreach (var (actual, returnRequest) in returnPairs)
            {
                if (actual.Amount >= returnRequest.Amount)
                {
                    actual.Amount -= returnRequest.Amount;
                    appliedReturns.Add(returnRequest);
                }
            }

            var cardsReturning = appliedReturns
                .Select(cr => new CardReturn(cr.Card, cr.Amount, deck))
                .ToList();

            foreach (var ret in appliedReturns)
            {
                ret.Amount = 0;
            }

            return cardsReturning;
        }


        private void RemoveEmpty(Deck deck)
        {
            var emptyAmounts = deck.Cards.Where(ca => ca.Amount == 0);
            var finishedRequests = deck.Wants.Where(cr => cr.Amount == 0);

            // do not remove empty availables
            _dbContext.Amounts.RemoveRange(emptyAmounts);
            _dbContext.Requests.RemoveRange(finishedRequests);
        }
    }
}