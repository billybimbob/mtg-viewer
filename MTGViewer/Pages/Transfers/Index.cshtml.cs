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

        public IReadOnlyList<Deck> ReceivedTrades { get; private set; }
        public IReadOnlyList<Deck> RequestDecks { get; private set; }

        public IReadOnlyList<Suggestion> Suggestions { get; private set; }



        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            var userDecks = await DeckForTransfers(userId).ToListAsync();

            SelfUser = await _dbContext.Users.FindAsync(userId);

            ReceivedTrades = userDecks
                .Where(d => d.TradesFrom.Any())
                .ToList();

            RequestDecks = userDecks
                .Where(d => d.TradesTo.Any() || d.Requests.Any(cr => !cr.IsReturn))
                .ToList();

            Suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)
                .Include(s => s.Card)
                .Include(s => s.To)
                .OrderBy(s => s.Card.Name)
                .ToListAsync();
        }


        public IQueryable<Deck> DeckForTransfers(string userId)
        {
            return _dbContext.Decks
                .Where(d => d.OwnerId == userId)

                .Include(d => d.TradesFrom)
                .Include(d => d.TradesTo)

                .Include(d => d.Requests
                    .Where(cr => !cr.IsReturn))
                    .ThenInclude(cr => cr.Card)

                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)

                .OrderBy(d => d.Name)
                .AsSplitQuery();
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