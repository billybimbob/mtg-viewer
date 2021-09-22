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
        public IEnumerable<RequestGroup> CardGroups { get; private set; }

        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var deck = await DeckWithCardsAndExchanges(deckId).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);

            var deckRequests = deck.ExchangesTo
                .Where(ex => !ex.IsTrade)
                .Concat(deck.ExchangesFrom);

            var cardGroups = deck.Cards
                .GroupJoin( deckRequests,
                    ca => ca.CardId,
                    ex => ex.CardId,
                    (amount, requests) => 
                        new RequestGroup(amount, requests));


            Deck = deck;

            CanEdit = deck.OwnerId == userId 
                && !deck.ExchangesTo.Any(ex => ex.IsTrade);

            CardGroups = cardGroups.ToList();

            return Page();
        }


        private IQueryable<Deck> DeckWithCardsAndExchanges(int deckId)
        {
            var deckWithOwner = _dbContext.Decks
                .Where(d => d.Id == deckId)
                .Include(d => d.Owner);

            var withCards = deckWithOwner
                .Include(d => d.Cards
                    .OrderBy(ca => ca.Card.Name))
                    .ThenInclude(ca => ca.Card);

            var withTos = withCards
                .Include(d => d.ExchangesTo
                    .OrderBy(ex => ex.Card.Name))
                    .ThenInclude(ex => ex.Card);

            var withReturns = withTos
                .Include(d => d.ExchangesFrom
                    .Where(ex => !ex.IsTrade)
                    .OrderBy(ex => ex.Card.Name))
                    .ThenInclude(ca => ca.Card);

            return withReturns
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }
    }
}