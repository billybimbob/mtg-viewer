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


        public bool IsOwner { get; private set; }

        public Deck Deck { get; private set; }
        public IEnumerable<AmountRequestGroup> Cards { get; private set; }

        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var deck = await DeckForViewer(deckId).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);

            IsOwner = deck.OwnerId == userId;
            Deck = deck;
            Cards = DeckCardGroups(deck).ToList();

            return Page();
        }


        private IQueryable<Deck> DeckForViewer(int deckId)
        {
            return _dbContext.Decks
                .Where(d => d.Id == deckId)

                .Include(d => d.Owner)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)

                .Include(d => d.TradesTo)
                    .ThenInclude(t => t.Card)

                .Include(d => d.Requests)
                    .ThenInclude(cr => cr.Card)

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private IEnumerable<AmountRequestGroup> DeckCardGroups(Deck deck)
        {
            var amountsById = deck.Cards
                .ToDictionary(ca => ca.CardId);

            var requestsById = deck.Requests
                .ToLookup(ex => ex.CardId);

            var cardIds = amountsById
                .Select(g => g.Key)
                .Union(requestsById
                    . Select(g => g.Key));

            return cardIds
                .Select(cid =>
                {
                    amountsById.TryGetValue(cid, out var amount);
                    return new AmountRequestGroup(amount, requestsById[cid]);
                })
                .OrderBy(rg => rg.Card.Name);
        }
    }
}