using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    public class ViewerModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public ViewerModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        public bool CanEdit { get; private set; }
        public Deck Deck { get; private set; }
        public IEnumerable<AmountPair> Amounts { get; private set; }

        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            Deck = await _dbContext.Decks
                .Include(d => d.Owner)
                .Include(d => d.Cards
                    .OrderBy(ca => ca.Card.Name))
                    .ThenInclude(ca => ca.Card)
                .Include(d => d.ToRequests
                    .Where(t => t is Trade && t.ProposerId == t.To.OwnerId))
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(d => d.Id == deckId);

            if (Deck == default)
            {
                return NotFound();
            }

            CanEdit = Deck.OwnerId == _userManager.GetUserId(User)
                && !Deck.ToRequests.Any();

            Amounts = Deck.Cards
                .GroupBy(ca => ca.CardId,
                    (_, amounts) => new AmountPair(amounts))
                .ToList();

            return Page();
        }
    }
}