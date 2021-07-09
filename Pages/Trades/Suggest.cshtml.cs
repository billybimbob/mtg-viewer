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
    public class SuggestModel : PageModel
    {
        private const string CARD_ID = "CardId";

        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        public SuggestModel(CardDbContext dbContext, UserManager<CardUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }


        private string CardId
        {
            // gets casted to guid for some reason
            get => TempData[CARD_ID].ToString();
            set => TempData[CARD_ID] = value;
        }

        [TempData]
        public string PostMessage { get; set; }

        public IEnumerable<CardUser> Users { get; private set; }

        public IEnumerable<(Location, IEnumerable<string>)> Decks { get; private set; }

        public Card Suggesting { get; private set; }

        private async Task SetSuggestingAsync() =>
            Suggesting = await _dbContext.Cards.FindAsync(CardId);


        public async Task<IActionResult> OnGetAsync(string cardId)
        {
            CardId = cardId;
            TempData.Keep(CARD_ID);

            await SetSuggestingAsync();

            if (Suggesting == null)
            {
                return NotFound();
            }

            var srcId = _userManager.GetUserId(User);

            Users = await _userManager.Users
                .Where(u => u.Id != srcId)
                .ToListAsync();

            return Page();
        }


        public async Task<IActionResult> OnPostUserAsync(string userId)
        {
            await SetSuggestingAsync();

            if (Suggesting == null)
            {
                return NotFound();
            }

            var decks = await _dbContext.Locations
                .Where(l => l.OwnerId == userId)
                .Include(d => d.Owner)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)
                        .ThenInclude(c => c.Colors)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            var deckColors = decks
                .Select(d => d
                    .GetColors()
                    .Select(c => Color.COLORS[ c.Name.ToLower() ]));

            Decks = decks.Zip(deckColors);

            TempData.Keep(CARD_ID);

            return Page();
        }


        public async Task<IActionResult> OnPostDeckAsync(int deckId)
        {
            await SetSuggestingAsync();

            if (Suggesting == null)
            {
                return NotFound();
            }

            var destLoc = await _dbContext.Locations.FindAsync(deckId);

            if (destLoc == null || destLoc.IsShared)
            {
                return NotFound();
            }

            await _dbContext.Entry(destLoc)
                .Reference(d => d.Owner)
                .LoadAsync();

            var srcUser = await _userManager.GetUserAsync(User);

            var suggestion = new Trade
            {
                Card = Suggesting,
                FromUser = srcUser,
                ToUser = destLoc.Owner,
                To = destLoc
            };

            _dbContext.Attach(suggestion);

            await _dbContext.SaveChangesAsync();

            PostMessage = "Suggestion Successfully Created";

            return RedirectToPage("./Index");
        }
    }

}