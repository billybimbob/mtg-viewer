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
    public record DeckTrade(Deck Deck, int NumberOfTrades) { }


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
        public IReadOnlyList<DeckTrade> SentTrades { get; private set; }

        public IReadOnlyList<Deck> PossibleTrades { get; private set; }

        public IReadOnlyList<Suggestion> Suggestions { get; private set; }



        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);
            var userDecks = await DeckWithTakesAndTrades(userId).ToListAsync();

            SelfUser = await _dbContext.Users.FindAsync(userId);

            ReceivedTrades = userDecks
                .SelectMany(d => d.TradesFrom)
                .GroupBy(t => t.To,
                    (to, trades) => new DeckTrade(to, trades.Count()))
                .OrderBy(dt => dt.Deck.Name)
                .ToList();

            SentTrades = userDecks
                .SelectMany(d => d.TradesTo)
                .GroupBy(t => t.From,
                    (from, trades) => new DeckTrade(from, trades.Count()))
                .OrderBy(dt => dt.Deck.Name)
                .ToList();

            var sentDecks = SentTrades.Select(dt => dt.Deck);

            PossibleTrades = userDecks
                .SelectMany(
                    d => d.Requests.Where(cr => !cr.IsReturn),
                    (_, take) => take.Target)
                .Except(sentDecks)
                .OrderBy(d => d.Name)
                .ToList();

            Suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)
                .Include(s => s.Card)
                .Include(s => s.To)
                .OrderBy(s => s.Card.Name)
                .ToListAsync();
        }


        public IQueryable<Deck> DeckWithTakesAndTrades(string userId)
        {
            return _dbContext.Decks
                .Where(d => d.OwnerId == userId)
                .AsSplitQuery()

                .Include(d => d.Requests)
                    .ThenInclude(cr => cr.Card)
                .Include(d => d.Requests)
                    .ThenInclude(cr => cr.Target)

                .Include(d => d.Requests
                    .Where(cr => !cr.IsReturn))

                .Include(d => d.TradesTo)
                    .ThenInclude(t => t.Card)
                .Include(d => d.TradesTo)
                    .ThenInclude(t => t.From)

                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.Card)
                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.To);
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