using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Trades
{
    [Authorize]
    public class ReviewModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        public ReviewModel(CardDbContext dbContext, UserManager<CardUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }


        [TempData]
        public string PostMessage { get; set; }

        public CardUser Proposer { get; private set; }
        public Deck Deck { get; private set; }

        public IReadOnlyList<Trade> ToDeck { get; private set; }
        public IReadOnlyList<Trade> FromDeck { get; private set; }


        public async Task<IActionResult> OnGetAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Decks.FindAsync(deckId);

            if (deck == null || deck.OwnerId == proposerId)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.NotSuggestion)
                .Where(TradeFilter.Involves(proposerId, deckId))
                .Include(t => t.Card)
                .Include(t => t.To)
                .Include(t => t.From)
                .AsNoTracking()
                .ToListAsync();

            if (!CheckTrades(deckTrades))
            {
                PostMessage = "Cannot find any trades";
                return RedirectToPage("./Index");
            }

            await _dbContext.Entry(deck)
                .Reference(l => l.Owner)
                .LoadAsync();

            Deck = deck;

            Proposer = await _userManager.FindByIdAsync(proposerId);

            ToDeck = deckTrades
                .Where(t => t.To.Id == deckId)
                .OrderBy(t => t.Card.Name)
                .ToList();

            FromDeck = deckTrades
                .Except(ToDeck)
                .OrderBy(t => t.Card.Name)
                .ToList();

            return Page();
        }


        private bool CheckTrades(IEnumerable<Trade> trades)
        {
            if (!trades.Any())
            {
                return false;
            }

            var userId = _userManager.GetUserId(User);

            if (!trades.All(t => t.IsInvolved(userId)))
            {
                return false;
            }

            if (!trades.All(t => t.IsWaitingOn(userId)))
            {
                return false;
            }

            return true;
        }


        public async Task<IActionResult> OnPostAcceptAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Decks.FindAsync(deckId);

            if (deck == null || deck.OwnerId == proposerId)
            {
                return NotFound();
            }
            
            var acceptQuery = _dbContext.Trades
                .Where(TradeFilter.NotSuggestion)
                .Where(TradeFilter.Involves(proposerId, deckId));

            var deckTrades = await acceptQuery
                .ToListAsync();

            if (!CheckTrades(deckTrades))
            {
                PostMessage = "Cannot find any trades to Accept";
                return RedirectToPage("./Index");
            }

            var nonRequestAmounts = _dbContext.Amounts
                .Where(ca => !ca.IsRequest);

            var sourceMap = await acceptQuery
                .Join(nonRequestAmounts,
                    t =>
                        new { t.CardId, DeckId = t.FromId },
                    ca =>
                        new { ca.CardId, DeckId = ca.LocationId },
                    (trade, amount) =>
                        new { trade.Id, amount })
                .ToDictionaryAsync(r => r.Id, r => r.amount);


            var amountsInvalid = deckTrades.Any(t => 
                !sourceMap.TryGetValue(t.Id, out var source) || source.Amount < t.Amount);

            if (amountsInvalid)
            {
                PostMessage = "Source Deck lacks the required amount to complete the trade";
                return RedirectToPage("./Index");
            }

            var destMap = await acceptQuery
                .Join(nonRequestAmounts,
                    t =>  new { t.CardId, DeckId = t.ToId },
                    ca => new { ca.CardId, DeckId = ca.LocationId },
                    (trade, amount) => new { trade.Id, amount })
                .ToDictionaryAsync(t => t.Id, t => t.amount);

            ApplyAccepts(deckTrades, sourceMap, destMap);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Trade successfully Applied";
            }
            catch(DbUpdateConcurrencyException)
            {
                PostMessage = "Ran into error while Accepting";
            }
            catch(DbUpdateException)
            {
                PostMessage = "Ran into error while Accepting";
            }

            return RedirectToPage("./Index");
        }


        private void ApplyAccepts(
            IReadOnlyList<Trade> accepts,
            IReadOnlyDictionary<int, CardAmount> sourceMap,
            IDictionary<int, CardAmount> destMap)
        {
            foreach(var accept in accepts)
            {
                if (!destMap.TryGetValue(accept.Id, out var destAmount))
                {
                    destAmount = new CardAmount
                    {
                        Card = accept.Card,
                        Location = accept.To
                    };

                    destMap.Add(accept.Id, destAmount);
                    _dbContext.Amounts.Attach(destAmount);
                }

                sourceMap[accept.Id].Amount -= accept.Amount;
                destAmount.Amount += accept.Amount;
            }

            _dbContext.Trades.RemoveRange(accepts);
        }


        public async Task<IActionResult> OnPostRejectAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Decks.FindAsync(deckId);

            if (deck == null || deck.OwnerId == proposerId)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.NotSuggestion)
                .Where(TradeFilter.Involves(proposerId, deckId))
                .ToListAsync();

            if (!CheckTrades(deckTrades))
            {
                PostMessage = "Cannot find any trades to Reject";
                return RedirectToPage("./Index");
            }

            _dbContext.Trades.RemoveRange(deckTrades);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully rejected Trade";
            }
            catch(DbUpdateConcurrencyException)
            {
                PostMessage = "Ran into error while rejecting";
            }
            catch(DbUpdateException)
            {
                PostMessage = "Ran into error while rejecting";
            }

            return RedirectToPage("./Index");
        }
    }
}