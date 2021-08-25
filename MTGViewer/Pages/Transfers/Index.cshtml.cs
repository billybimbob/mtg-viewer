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
        public record DeckTrade(Deck Deck, int NumberOrTrades) { }


        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public IndexModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        [TempData]
        public string PostMessage { get; set; }

        public CardUser SelfUser { get; set; }

        public IReadOnlyList<DeckTrade> ReceivedTrades { get; private set; }
        public IReadOnlyList<DeckTrade> PendingTrades { get; private set; }
        public IReadOnlyList<DeckTrade> PossibleRequests { get; private set; }

        public IReadOnlyList<Transfer> Suggestions { get; private set; }



        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            var userTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(userId))
                .Include(t => t.To)
                .Include(t => t.From)
                .ToListAsync();

            // var requestDecks = await _dbContext.Decks
            //     .Where(d => d.OwnerId == userId && d.Cards.Any(ca => ca.IsRequest))
            //     .ToListAsync();

            var requestDecks = await _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId == userId)
                .GroupBy(ca => ca.LocationId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .Join(_dbContext.Decks,
                    lc => lc.Id,
                    d => d.Id,
                    (lc, deck) => new { deck, count = lc.Count })
                .Select(dc => new DeckTrade(dc.deck, dc.count))
                .ToListAsync();


            SelfUser = await _userManager.FindByIdAsync(userId);

            ReceivedTrades = userTrades
                .Where(t => t.ReceiverId == userId)
                .GroupBy(t => t.From)
                .Select(g => new DeckTrade(g.Key, g.Count()))
                .OrderBy(t => t.Deck.Name)
                .ToList();

            PendingTrades = userTrades
                .Where(t => t.ProposerId == userId)
                .GroupBy(t => t.To)
                .Select(g => new DeckTrade(g.Key, g.Count()))
                .OrderBy(t => t.Deck.Name)
                .ToList();

            PossibleRequests = requestDecks
                .Except(PendingTrades, new EntityComparer<DeckTrade>(dt => dt.Deck))
                .ToList();

            Suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)
                .Include(s => s.Card)
                .Include(s => s.Proposer)
                .Include(s => s.To)
                .ToListAsync();
        }



        public async Task<IActionResult> OnPostAsync(int suggestId)
        {
            var userId = _userManager.GetUserId(User);
            var suggestion = await _dbContext.Suggestions
                .SingleOrDefaultAsync(s => s.Id == suggestId && s.ReceiverId == userId);

            if (suggestion is null)
            {
                PostMessage = "Specified suggestion cannot be acknowledged";
                return RedirectToPage("./Index");
            }

            _dbContext.Entry(suggestion).State = EntityState.Deleted;

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Suggestion Acknowledged";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into issue while trying to Acknowledge";
            }

            return RedirectToPage("./Index");
        }
    }
}