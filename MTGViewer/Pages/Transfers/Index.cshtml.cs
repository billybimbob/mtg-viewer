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
    public record DeckTrade(Deck Deck, int NumberOrTrades) { }


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

        public UserRef SelfUser { get; set; }

        public IReadOnlyList<DeckTrade> ReceivedTrades { get; private set; }
        public IReadOnlyList<DeckTrade> PendingTrades { get; private set; }

        public IReadOnlyList<Deck> PossibleRequests { get; private set; }
        public IReadOnlyList<Suggestion> Suggestions { get; private set; }



        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            var userTrades = await _dbContext.Trades
                .Where(t => t.ProposerId == userId || t.ReceiverId == userId)
                .Include(t => t.To)
                .Include(t => t.From)
                .ToListAsync();

            var requestDecks = await _dbContext.Decks
                .Where(d => d.OwnerId == userId
                    && d.Cards.Any(da => da.Intent == Intent.Take))
                .Include(d => d.Cards
                    .Where(da => da.Intent == Intent.Take))
                .Include(d => d.Owner)
                .ToListAsync();


            SelfUser = requestDecks.FirstOrDefault()?.Owner
                ?? await _dbContext.Users.FindAsync(userId);

            ReceivedTrades = userTrades
                .Where(t => t.ReceiverId == userId)
                .GroupBy(t => t.From,
                    (from, trades) => new DeckTrade(from, trades.Count()) )
                .OrderBy(t => t.Deck.Name)
                .ToList();

            PendingTrades = userTrades
                .Where(t => t.ProposerId == userId)
                .GroupBy(t => t.To,
                    (to, trades) => new DeckTrade(to, trades.Count()) )
                .OrderBy(t => t.Deck.Name)
                .ToList();

            PossibleRequests = requestDecks
                .Except(PendingTrades.Select(dt => dt.Deck))
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
                .SingleOrDefaultAsync(s =>
                    s.Id == suggestId && s.ReceiverId == userId);

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