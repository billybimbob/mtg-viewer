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

        public IReadOnlyList<DeckTrade> TakeFroms { get; private set; }
        public IReadOnlyList<DeckTrade> PendingTakeTos { get; private set; }
        public IReadOnlyList<Deck> PossibleTakeTos { get; private set; }

        public IReadOnlyList<Suggestion> Suggestions { get; private set; }



        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            var userExchanges = await _dbContext.Exchanges
                .Where(ex => ex.To.OwnerId == userId
                    || ex.From.OwnerId == userId && ex.IsTrade)
                .Include(ex => ex.To.Owner)
                .Include(ex => ex.From.Owner)
                .Include(ex => ex.Card)
                .ToListAsync();

            SelfUser = await _dbContext.Users.FindAsync(userId);

            TakeFroms = userExchanges
                .Where(ex => ex.IsTrade
                    && ex.From != default && ex.From.OwnerId == userId)
                .GroupBy(t => t.From,
                    (from, trades) => new DeckTrade(from, trades.Count()) )
                .OrderBy(t => t.Deck.Name)
                .ToList();

            var userTakes = userExchanges
                .Where(ex => ex.To != default && ex.To.OwnerId == userId);

            PendingTakeTos = userTakes
                .Where(ex => ex.IsTrade)
                .GroupBy(t => t.To,
                    (to, trades) => new DeckTrade(to, trades.Count()) )
                .OrderBy(t => t.Deck.Name)
                .ToList();

            var pendingDecks = PendingTakeTos.Select(dt => dt.Deck);

            PossibleTakeTos = userTakes
                .Where(ex => !ex.IsTrade)
                .Select(ex => ex.To)
                .Except(pendingDecks)
                .OrderBy(d => d.Name)
                .ToList();

            Suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)
                .Include(s => s.Card)
                .Include(s => s.To)
                .OrderBy(s => s.Card.Name)
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