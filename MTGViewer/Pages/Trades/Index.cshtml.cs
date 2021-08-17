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
    public class IndexModel : PageModel
    {
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

        public IReadOnlyList<(CardUser, Deck)> ReceivedTrades { get; private set; }
        public IReadOnlyList<(CardUser, Deck)> PendingTrades { get; private set; }
        public IReadOnlyList<Transfer> Suggestions { get; private set; }


        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            var userTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(userId))
                .Include(t => t.TargetDeck)
                    .ThenInclude(l => l.Owner)
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


        private IReadOnlyList<(CardUser, Deck)> GetTradeList(string userId, IEnumerable<Trade> trades)
        {
            return trades.GroupBy(t => t.TargetDeck)
                .Select(g =>
                    (OtherUser: g.First().GetOtherUser(userId), Target: g.Key))
                .OrderBy(t => t.OtherUser.Name)
                    .ThenBy(t => t.Target.Name)
                .ToList();
        }


        public async Task<IActionResult> OnPostAsync(int suggestId)
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