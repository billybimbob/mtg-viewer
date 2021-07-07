using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _context;

        public IndexModel(UserManager<CardUser> userManager, CardDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public CardUser CardUser { get; private set; }
        public IEnumerable<Location> Decks { get; private set; }
        public IEnumerable<IEnumerable<string>> DeckColors { get; private set; }


        public async Task OnGet()
        {
            CardUser = await _userManager.GetUserAsync(User);

            Decks = await _context.Locations
                .Where(l => l.Owner == CardUser)
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                        .ThenInclude(c => c.Colors)
                .AsSplitQuery()
                .AsNoTracking()
                .ToListAsync();

            DeckColors = Decks.Select(d => d
                .GetColors()
                .Select(c => Color.COLORS[c.Name.ToLower()]));
        }

    }
}