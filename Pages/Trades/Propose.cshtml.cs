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


        public string UserId { get; private set; }
        public int DeckId { get; private set; }

        public IReadOnlyList<Location> ProposeOptions { get; private set; }

        public async Task<IActionResult> OnGetAsync(int? deckId)
        {
            UserId = _userManager.GetUserId(User);

            if (deckId is int id)
            {
                var deck = await _dbContext.Locations.FindAsync(id);

                if (!deck?.IsShared ?? false)
                {
                    DeckId = id;
                }
            }

            if (DeckId == default)
            {
                ProposeOptions = await _dbContext.Locations
                    .Where(l => l.OwnerId != default && l.OwnerId != UserId)
                    .ToListAsync();

                if (!ProposeOptions.Any())
                {
                    return NotFound();
                }
            }

            return Page();
        }
    }
}