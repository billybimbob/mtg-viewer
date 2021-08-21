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


namespace MTGViewer.Pages.Transfers
{
    [Authorize]
    public class IndexModel : PageModel
    {
        public record DeckTrade(CardUser OtherUser, Deck Target) { }


        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public IndexModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        [TempData]
        public string PostMessage { get; set; }

        public CardUser SelfUser { get; private set; }

        public IReadOnlyList<DeckTrade> ReceivedTrades { get; private set; }
        public IReadOnlyList<DeckTrade> PendingTrades { get; private set; }
        public IReadOnlyList<Transfer> Suggestions { get; private set; }


        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            var userTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(userId))
                .Include(t => t.Proposer)
                .Include(t => t.Receiver)
                .Include(t => t.TargetDeck.Owner)
                .ToListAsync();

            var waitingUser = userTrades
                .Where(t => t.IsWaitingOn(userId));

            SelfUser = await _userManager.FindByIdAsync(userId);

            ReceivedTrades = GetTradeList(userId, waitingUser);

            PendingTrades = GetTradeList(userId, userTrades.Except(waitingUser));

            Suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)
                .Include(s => s.Card)
                .Include(s => s.Proposer)
                .Include(s => s.To)
                .ToListAsync();
        }


        private IReadOnlyList<DeckTrade> GetTradeList(string userId, IEnumerable<Trade> trades)
        {
            return trades
                .GroupBy(t => t.TargetDeck)
                .Select(g =>
                    new DeckTrade(
                        g.First().GetOtherUser(userId),
                        g.Key))
                .OrderBy(t => t.OtherUser.Name)
                    .ThenBy(t => t.Target.Name)
                .ToList();
        }


        public async Task<IActionResult> OnPostCancelAsync(string proposerId, int deckId)
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
                .Where(TradeFilter.Involves(proposerId, deckId))
                .ToListAsync();

            var userId = _userManager.GetUserId(User);

            var tradesValid = deckTrades.Any()
                && deckTrades.All(t => t.IsInvolved(userId))
                && deckTrades.All(t => !t.IsWaitingOn(userId));

            if (!tradesValid)
            {
                PostMessage = "Not all specified trades are valid";
                return RedirectToPage("./Index");
            }

            _dbContext.Trades.RemoveRange(deckTrades);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully rejected Trade";
            }
            catch (DbUpdateConcurrencyException)
            {
                PostMessage = "Ran into error while rejecting";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while rejecting";
            }

            return RedirectToPage("./Index");
        }


        public async Task<IActionResult> OnPostAckAsync(int suggestId)
        {
            var suggestion = await _dbContext.Suggestions.FindAsync(suggestId);
            var userId = _userManager.GetUserId(User);

            if (suggestion is null || suggestion.ReceiverId != userId)
            {
                PostMessage = "Specified suggestion cannot be acknowledged";
            }
            else
            {
                _dbContext.Entry(suggestion).State = EntityState.Deleted;

                await _dbContext.SaveChangesAsync();

                PostMessage = "Suggestion Acknowledged";
            }

            return RedirectToPage("./Index");
        }
    }
}