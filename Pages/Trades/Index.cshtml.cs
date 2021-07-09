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

        public IReadOnlyList<Trade> PendingTrades { get; private set; }
        public IReadOnlyList<Trade> Suggestions { get; private set; }

        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            PendingTrades = await _dbContext.Trades
                .Where(TradeFilter.PendingFor(userId))
                // .Where(t => t.SrcLocationId != default
                //     && (t.DestUser.Id == userId && !t.IsCounter 
                //         || t.SrcUser.Id == userId && t.IsCounter))
                .Include(t => t.Card)
                .Include(t => t.FromUser)
                .Include(t => t.To)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            Suggestions = await _dbContext.Trades
                .Where(TradeFilter.SuggestionFor(userId))
                // .Where(t => t.SrcLocationId == default && t.DestUser.Id == userId)
                .Include(t => t.Card)
                .Include(t => t.FromUser)
                .Include(t => t.To)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();
        }


        public async Task<IActionResult> OnPostAcceptAsync(int tradeId)
        {
            var trade = await _dbContext.Trades.FindAsync(tradeId);

            if (trade == null || trade.IsSuggestion)
            {
                return NotFound();
            }

            await _dbContext.Entry(trade)
                .Reference(t => t.From)
                .LoadAsync();
                
            if (trade.From.Amount < trade.Amount)
            {
                PostMessage = "Source Deck lacks the trade amount to complete the trade";
                return RedirectToPage("./Index");
            }

            var destAmount = await _dbContext.Amounts
                .SingleOrDefaultAsync(ca =>
                    ca.CardId == trade.CardId
                        && ca.LocationId == trade.ToId
                        && !ca.IsRequest);

            if (destAmount == null)
            {
                destAmount = new CardAmount
                {
                    CardId = trade.CardId,
                    LocationId = trade.ToId
                };

                _dbContext.Attach(destAmount);
            }

            trade.From.Amount -= trade.Amount;
            destAmount.Amount += trade.Amount;

            _dbContext.Remove(trade);

            await _dbContext.SaveChangesAsync();

            PostMessage = "Trade Successfully Applied";

            return RedirectToPage("./Index");
        }


        public async Task<IActionResult> OnPostRejectAsync(int tradeId)
        {
            var trade = await _dbContext.Trades.FindAsync(tradeId);

            if (trade == null || trade.IsSuggestion)
            {
                return NotFound();
            }

            _dbContext.Entry(trade).State = EntityState.Deleted;

            await _dbContext.SaveChangesAsync();

            PostMessage = "Trade Successfully Deleted";

            return RedirectToPage("./Index");
        }


        public async Task<IActionResult> OnPostAckAsync(int suggestId)
        {
            var suggestion = await _dbContext.Trades.FindAsync(suggestId);

            if (suggestion == null || !suggestion.IsSuggestion)
            {
                return NotFound();
            }

            _dbContext.Entry(suggestion).State = EntityState.Deleted;

            await _dbContext.SaveChangesAsync();

            PostMessage = "Suggestion Acknowledged";

            return RedirectToPage("./Index");
        }
    }
}