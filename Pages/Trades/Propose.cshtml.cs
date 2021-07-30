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
    public class ProposeModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public ProposeModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        public CardUser CardUser { get; private set; }
        public int DeckId { get; private set; }
        public IReadOnlyList<Location> ProposeOptions { get; private set; }


        public async Task<IActionResult> OnGetAsync(int? deckId)
        {
            CardUser = await _userManager.GetUserAsync(User);

            if (deckId is int id)
            {
                var deck = await _dbContext.Locations.FindAsync(id);

                bool validDeck = deck != null 
                    && !deck.IsShared 
                    && deck.OwnerId != CardUser.Id;

                if (validDeck)
                {
                    DeckId = id;
                }
            }

            if (DeckId == default)
            {
                ProposeOptions = await GetProposeOptionsAsync();

                if (!ProposeOptions.Any())
                {
                    return NotFound();
                }
            }

            return Page();
        }


        private async Task<IReadOnlyList<Location>> GetProposeOptionsAsync()
        {
            var nonUserLocs = await _dbContext.Locations
                .Where(l => l.OwnerId != default && l.OwnerId != CardUser.Id)
                .Include(l => l.Owner)
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            var currentTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(CardUser.Id))
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            var tradeLocs = currentTrades
                .SelectMany(t => t.GetLocations())
                .Distinct();

            return nonUserLocs
                .Except(tradeLocs)
                .OrderBy(l => l.Owner.Name)
                    .ThenBy(l => l.Name)
                .ToList();
        }
    }
}