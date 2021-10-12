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
            IMTGSymbols mtgSymbols,
            ILogger<CheckoutModel> logger)
        {
            _dbContext = dbContext;
            _sharedStorage = sharedStorage;
            _userManager = userManager;

            MtgSymbols = mtgSymbols;
            _logger = logger;
        }


        [TempData]
        public string? PostMessage { get; set; }

        public IMTGSymbols MtgSymbols { get; }

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

            HasPendings = deck.GiveBacks.Any() 
                || await TakeTargets(deck).AnyAsync();

            return Page();
        }


        private IQueryable<Deck> DeckForCheckout(int deckId)
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

                .Include(d => d.TradesTo.Take(1))
                .AsSplitQuery();
        }


        private IQueryable<CardAmount> TakeTargets(Deck deck)
        {
            var takeNames = deck.Wants
                .Select(w => w.Card.Name)
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

            var availables = await TakeTargets(deck)
                .ToListAsync(); // unbounded, keep eye on, or limit

            ApplyWants(deck, availables);

            var boxReturns = ApplyDeckReturns(deck);

            RemoveEmpty(deck);

            deck.UpdateColors(MtgSymbols);

            var requestsRemain = deck.Wants.Sum(w => w.Amount) 
                + deck.GiveBacks.Sum(g => g.Amount);

            try
            {
                if (boxReturns.Any())
                {
                    await _sharedStorage.ReturnAsync(boxReturns);
                }

                await _dbContext.SaveChangesAsync();

                if (requestsRemain == 0)
                {
                    PostMessage = "Successfully exchanged all card requests";
                }
                else
                {
                    PostMessage = "Successfully exchanged requests, "
                        + "but not all could be fullfilled";
                }
            }
            catch (DbUpdateException e)
            {
                _logger.LogError($"ran into db error {e}");
                PostMessage = "Ran into issue while trying to checkout";
            }

            return RedirectToPage("History", new { id });
        }



        private void ApplyWants(Deck deck, IEnumerable<CardAmount> availables)
        {
            var activeWants = deck.Wants.Where(w => w.Amount > 0);
            var possibleAvails = availables.Where(ca => ca.Amount > 0);

            if (!possibleAvails.Any() || !activeWants.Any())
            {
                return;
            }

            var transaction = new Transaction();
            var allActuals = GetAllActuals(deck, availables);

            _dbContext.Transactions.Attach(transaction);

            ApplyExactWants(transaction, activeWants, possibleAvails, allActuals);
            ApplyCloseWants(transaction, activeWants, possibleAvails, allActuals);
        }


        private IReadOnlyDictionary<string, CardAmount> GetAllActuals(
            Deck deck, IEnumerable<CardAmount> availables)
        {
            var missingActualCards = availables
                .GroupJoin( deck.Cards,
                    available => available.CardId,
                    actual => actual.CardId,
                    (available, actuals) => (available, actuals))

                .Where(aas => !aas.actuals.Any())
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


        private void ApplyExactWants(
            Transaction transaction,
            IEnumerable<Want> wants,
            IEnumerable<CardAmount> availables,
            IReadOnlyDictionary<string, CardAmount> actuals)
        {
            var exactMatches = wants
                .Join( availables,
                    want => want.CardId,
                    avail => avail.CardId,
                    (want, avail) => (want, avail));

            foreach (var (want, available) in exactMatches)
            {
                var actual = actuals[want.CardId];
                int amountTaken = Math.Min(want.Amount, available.Amount);

                actual.Amount += amountTaken;
                available.Amount -= amountTaken;
                want.Amount -= amountTaken;

                var newChange = new Change
                {
                    Card = available.Card,

                    From = available.Location,
                    To = actual.Location,
                    Amount = amountTaken,

                    Transaction = transaction
                };

                _dbContext.Changes.Attach(newChange);
            }
        }


        private void ApplyCloseWants(
            Transaction transaction,
            IEnumerable<Want> wants,
            IEnumerable<CardAmount> availables,
            IReadOnlyDictionary<string, CardAmount> actuals)
        {
            var wantsByName = wants
                .GroupBy(w => w.Card.Name,
                    (_, ws) => new WantNameGroup(ws));

            var availsByName = availables
                .GroupBy(ca => ca.Card.Name,
                    (_, cas) => new CardNameGroup(cas));

            var closeMatches = wantsByName
                .Join( availsByName,
                    wantGroup => wantGroup.Name,
                    availGroup => availGroup.Name,
                    (wantGroup, availGroup) => (wantGroup, availGroup));


            foreach (var (wantGroup, availGroup) in closeMatches)
            {
                using var closeAvails = availGroup.GetEnumerator();

                while (wantGroup.Amount > 0 && closeAvails.MoveNext())
                {
                    var currentAvail = closeAvails.Current;
                    var actual = actuals[currentAvail.CardId];

                    int amountTaken = Math.Min(wantGroup.Amount, currentAvail.Amount);

                    actual.Amount += amountTaken;

                    currentAvail.Amount -= amountTaken;
                    wantGroup.Amount -= amountTaken;

                    var newChange = new Change
                    {
                        Card = currentAvail.Card,

                        From = currentAvail.Location,
                        To = actual.Location,
                        Amount = amountTaken,

                        Transaction = transaction
                    };

                    _dbContext.Changes.Attach(newChange);
                }
            }
        }



        private IReadOnlyList<CardReturn> ApplyDeckReturns(Deck deck)
        {
            var returnPairs = deck.Cards
                .Join( deck.GiveBacks,
                    ca => ca.CardId,
                    gb => gb.CardId,
                    (actual, giveBack) => (actual, giveBack));

            // TODO: change to atomic returns, all or nothing
            var appliedReturns = new List<GiveBack>();

            foreach (var (actual, giveBack) in returnPairs)
            {
                if (actual.Amount >= giveBack.Amount)
                {
                    actual.Amount -= giveBack.Amount;
                    appliedReturns.Add(giveBack);
                }
            }

            var cardsReturning = appliedReturns
                .Select(g => new CardReturn(g.Card, g.Amount, deck))
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
            var finishedWants = deck.Wants.Where(r => r.Amount == 0);
            var finishedGives = deck.GiveBacks.Where(g => g.Amount == 0);

            // do not remove empty availables
            _dbContext.Amounts.RemoveRange(emptyAmounts);
            _dbContext.Wants.RemoveRange(finishedWants);
            _dbContext.GiveBacks.RemoveRange(finishedGives);
        }
    }
}