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
        public Location Deck { get; private set; }

        public IReadOnlyList<Trade> ToDeck { get; private set; }
        public IReadOnlyList<Trade> FromDeck { get; private set; }


        public async Task<IActionResult> OnGetAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Locations.FindAsync(deckId);

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
                    .ThenInclude(ca => ca.Location)
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

            var deck = await _dbContext.Locations.FindAsync(deckId);

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
                    .ThenInclude(ca => ca.Location)
                .ToListAsync();

            if (!CheckTrades(deckTrades))
            {
                PostMessage = "Cannot find any trades to Accept";
                return RedirectToPage("./Index");
            }

            var amountsInvalid = deckTrades.Any(t => t.From.Amount < t.Amount);

            if (amountsInvalid)
            {
                PostMessage = "Source Deck lacks the required amount to complete the trade";
                return RedirectToPage("./Index");
            }

            await ApplyAcceptsAsync(deckTrades);

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


        private async Task ApplyAcceptsAsync(IEnumerable<Trade> accepts)
        {
            var destIds = accepts
                .Select(t => t.ToId)
                .Distinct()
                .ToArray();

            var cardIds = accepts
                .Select(t => t.CardId)
                .Distinct()
                .ToArray();

            // TODO: find filter that pairs card with location
            var existing = await _dbContext.Amounts
                .Where(ca => cardIds.Contains(ca.CardId)
                    && destIds.Contains(ca.LocationId)
                    && !ca.IsRequest)
                .ToListAsync();

            var acceptPairs = accepts
                .Select(t => (t.CardId, t.ToId))
                .Distinct()
                .ToHashSet();

            var destMap = existing
                .Where(ca => acceptPairs.Contains((ca.CardId, ca.LocationId)))
                .ToDictionary(ca => (ca.CardId, ca.LocationId));

            foreach(var accept in accepts)
            {
                var key = (accept.CardId, accept.ToId);

                if (!destMap.TryGetValue(key, out var destAmount))
                {
                    destAmount = new CardAmount
                    {
                        Card = accept.Card,
                        Location = accept.To
                    };

                    destMap.Add(key, destAmount);
                    _dbContext.Amounts.Attach(destAmount);
                }

                accept.From.Amount -= accept.Amount;
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

            var deck = await _dbContext.Locations.FindAsync(deckId);

            if (deck == null || deck.OwnerId == proposerId)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.NotSuggestion)
                .Where(TradeFilter.Involves(proposerId, deckId))
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
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