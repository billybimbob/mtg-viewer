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


        [TempData]
        public string PostMessage { get; set; }

        public CardUser Proposer { get; private set; }
        public int DeckId { get; private set; }
        public IReadOnlyList<Location> ProposeOptions { get; private set; }

        public bool IsCounter =>
            _userManager.GetUserId(User) != Proposer?.Id;


        public async Task<IActionResult> OnGetAsync(string proposerId, int? deckId)
        {
            if (deckId is int id && proposerId != null)
            {
                var deck = await _dbContext.Locations.FindAsync(id);

                bool validDeck = deck != null
                    && deck.IsShared == false
                    && deck.OwnerId != proposerId;

                if (validDeck)
                {
                    DeckId = id;
                }
            }

            if (DeckId == default)
            {
                Proposer = await _userManager.GetUserAsync(User);
                ProposeOptions = await GetProposeOptionsAsync();

                if (!ProposeOptions.Any())
                {
                    PostMessage = "There are no decks to Trade with";
                    return RedirectToPage("./Index");
                }
            }
            else
            {
                Proposer = await _userManager.FindByIdAsync(proposerId);
            }

            return Page();
        }


        private async Task<IReadOnlyList<Location>> GetProposeOptionsAsync()
        {
            var nonUserLocs = await _dbContext.Locations
                .Where(l => l.OwnerId != default && l.OwnerId != Proposer.Id)
                .Include(l => l.Owner)
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();

            var currentTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(Proposer.Id))
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