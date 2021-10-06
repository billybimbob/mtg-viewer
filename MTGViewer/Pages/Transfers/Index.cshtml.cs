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

        public UserRef SelfUser { get; private set; }

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
                .Where(d => d.TradesTo.Any() || d.Wants.Any(cr => !cr.IsReturn))
                .ToList();

            Suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)
                .Include(s => s.Card)
                .Include(s => s.To)
                .OrderBy(s => s.SentAt)
                    .ThenBy(s => s.Card.Name)
                // unbounded: keep eye on, limit
                .ToListAsync();
        }


        public IQueryable<Deck> DeckForTransfers(string userId)
        {
            return _dbContext.Decks
                .Where(d => d.OwnerId == userId)

                .Include(d => d.Cards)
                    // unbounded: keep eye on
                    .ThenInclude(ca => ca.Card)

                .Include(d => d.Wants
                    // unbounded: keep eye on
                    .Where(cr => !cr.IsReturn))
                    .ThenInclude(cr => cr.Card)

                .Include(d => d.TradesFrom)
                    // unbounded: keep eye on

                .Include(d => d.TradesTo)
                    // unbounded: keep eye on

                .OrderBy(d => d.Name)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
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
                return RedirectToPage("Index");
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