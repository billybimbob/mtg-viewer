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
        public IEnumerable<RequestGroup> Cards { get; private set; }

        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var deck = await DeckWithCardsAndExchanges(deckId).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);

            Deck = deck;

            CanEdit = deck.OwnerId == userId 
                && !deck.ExchangesTo.Any(ex => ex.IsTrade);

            Cards = DeckCardGroups(deck).ToList();

            return Page();
        }


        private IQueryable<Deck> DeckWithCardsAndExchanges(int deckId)
        {
            return _dbContext.Decks
                .Where(d => d.Id == deckId)

                .Include(d => d.Owner)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)

                .Include(d => d.ExchangesTo)
                    .ThenInclude(ex => ex.Card)

                .Include(d => d.ExchangesFrom
                    .Where(ex => !ex.IsTrade))
                    .ThenInclude(ca => ca.Card)

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private IEnumerable<RequestGroup> DeckCardGroups(Deck deck)
        {
            var amountsById = deck.Cards
                .ToDictionary(ca => ca.CardId);

            var requestsById = deck.GetAllExchanges()
                .Where(ex => !ex.IsTrade)
                .ToLookup(ex => ex.CardId);

            var cardIds = amountsById
                .Select(g => g.Key)
                .Union(requestsById
                    . Select(g => g.Key));

            return cardIds
                .Select(cid =>
                {
                    amountsById.TryGetValue(cid, out var amount);
                    return new RequestGroup(amount, requestsById[cid]);
                })
                .OrderBy(rg => rg.Card.Name);
        }
    }
}