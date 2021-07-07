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

        private string CardId
        {
            get => TempData[CARD_ID].ToString();
            set => TempData[CARD_ID] = value;
        }


        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        public SuggestModel(CardDbContext dbContext, UserManager<CardUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }


        public Card Suggesting { get; private set; }

        public IEnumerable<CardUser> Users { get; private set; }

        public IEnumerable<(Location, IEnumerable<string>)> Decks { get; private set; }


        public async Task<IActionResult> OnGetAsync(string cardId)
        {
            Suggesting = await _dbContext.Cards.FindAsync(cardId);

            if (Suggesting == null)
            {
                return NotFound();
            }

            var srcId = _userManager.GetUserId(User);

            Users = await _userManager.Users
                .Where(u => u.Id != srcId)
                .ToListAsync();

            CardId = cardId;

            return Page();
        }


        public async Task<IActionResult> OnPostUserAsync(string userId)
        {
            var decks = await _dbContext.Locations
                .Where(l => l.OwnerId == userId)
                .Include(d => d.Owner)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)
                        .ThenInclude(c => c.Colors)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            if (!decks.Any())
            {
                return NotFound();
            }

            Decks = decks.Zip(decks.Select(d => d
                .GetColors()
                .Select(c => Color.COLORS[c.Name.ToLower()]) ));

            // gets casted to guid for some reason
            Suggesting = await _dbContext.Cards.FindAsync(CardId);

            if (Suggesting == null)
            {
                return NotFound();
            }

            TempData.Keep(CARD_ID);

            return Page();
        }


        public async Task<IActionResult> OnPostDeckAsync(int deckId)
        {
            var destLoc = await _dbContext.Locations.FindAsync(deckId);

            await _dbContext.Entry(destLoc)
                .Reference(d => d.Owner)
                .LoadAsync();

            if (destLoc == null)
            {
                return NotFound();
            }

            Suggesting = await _dbContext.Cards.FindAsync(CardId);

            if (Suggesting == null)
            {
                return NotFound();
            }

            var srcUser = await _userManager.GetUserAsync(User);

            var suggestion = new Trade
            {
                Card = Suggesting,
                SrcUser = srcUser,
                DestLocation = destLoc
            };

            _dbContext.Attach(suggestion);
            await _dbContext.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }

}