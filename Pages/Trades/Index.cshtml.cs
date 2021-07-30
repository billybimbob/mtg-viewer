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

        public IReadOnlyList<Location> ReceivedTrades { get; private set; }
        public IReadOnlyList<Location> PendingTrades { get; private set; }
        public IReadOnlyList<Trade> Suggestions { get; private set; }


        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            var userTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(userId))
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            var received = userTrades
                .Where(t => t.IsWaitingOn(userId));

            ReceivedTrades = received
                .SelectMany(t => t.GetLocations())
                .Distinct()
                .OrderBy(l => l.Owner.Name)
                    .ThenBy(l => l.Name)
                .ToList();

            PendingTrades = userTrades
                .Except(received)
                .SelectMany(t => t.GetLocations())
                .Distinct()
                .OrderBy(l => l.Owner.Name)
                    .ThenBy(l => l.Name)
                .ToList();

            Suggestions = await _dbContext.Trades
                .Where(TradeFilter.SuggestionFor(userId))
                .Include(t => t.Card)
                .Include(t => t.Proposer)
                .Include(t => t.To)
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();
        }


        public async Task<IActionResult> OnPostAsync(int suggestId)
        {
            var suggestion = await _dbContext.Trades.FindAsync(suggestId);

            if (suggestion is null || !suggestion.IsSuggestion)
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