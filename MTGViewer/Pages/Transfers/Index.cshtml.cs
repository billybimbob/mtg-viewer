using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;


namespace MTGViewer.Pages.Transfers
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly int _pageSize;
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public IndexModel(
            PageSizes pageSizes, 
            UserManager<CardUser> userManager, 
            CardDbContext dbContext)
        {
            _pageSize = pageSizes.GetSize(this);
            _userManager = userManager;
            _dbContext = dbContext;
        }


        [TempData]
        public string PostMessage { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? DeckIndex { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SuggestIndex { get; set; }


        public UserRef SelfUser { get; private set; }

        public PagedList<Deck> TradeDecks { get; private set; }

        public PagedList<Suggestion> Suggestions { get; private set; }



        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            TradeDecks = await DecksForTransfer(userId)
                .ToPagedListAsync(_pageSize, DeckIndex);

            SelfUser = await _dbContext.Users.FindAsync(userId);

            Suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)
                .Include(s => s.Card)
                .Include(s => s.To)
                .OrderBy(s => s.SentAt)
                    .ThenBy(s => s.Card.Name)
                .ToPagedListAsync(_pageSize, SuggestIndex);
        }


        public IQueryable<Deck> DecksForTransfer(string userId)
        {
            return _dbContext.Decks
                .Where(d => d.OwnerId == userId)

                .Where(d => d.TradesFrom.Any()
                    || d.TradesTo.Any()
                    || d.Wants.Any())

                .Include(d => d.TradesFrom
                    .OrderBy(t => t.Id)
                    .Take(1))

                .Include(d => d.TradesTo
                    .OrderBy(t => t.Id)
                    .Take(1))

                .Include(d => d.Wants
                    .OrderBy(w => w.Id)
                    .Take(1))

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

            return RedirectToPage("Index", new { DeckIndex, SuggestIndex });
        }
    }
}