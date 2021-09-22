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

#nullable enable

namespace MTGViewer.Pages.Transfers
{
    [Authorize]
    public class StatusModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public StatusModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        [TempData]
        public string? PostMessage { get; set; }

        public Deck? Destination { get; private set; }
        public UserRef? Proposer { get; private set; }

        public IReadOnlyList<Exchange>? Trades { get; private set; }
        public IReadOnlyList<RequestNameGroup>? CardGroups { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            if (deckId == default)
            {
                return NotFound();
            }

            var deck = await DeckWithCardsAndTos(deckId).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (!deck.ExchangesTo.Any(ex => !ex.IsTrade))
            {
                PostMessage = $"There are no requests for {deck.Name}";
                return RedirectToPage("./Index");
            }

            if (!deck.ExchangesTo.Any(ex => ex.IsTrade))
            {
                return RedirectToPage("./Request", new { deckId });
            }


            Destination = deck;

            Proposer = deck.Owner;

            Trades = deck.ExchangesTo
                .Where(ex => ex.IsTrade)
                .ToList();

            CardGroups = DeckNameGroups(deck).ToList();

            return Page();
        }


        private IQueryable<Deck> DeckWithCardsAndTos(int deckId)
        {
            var userId = _userManager.GetUserId(User);
            
            var deckWithOwner = _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)
                .Include(d => d.Owner);

            var withCards = deckWithOwner
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card);

            var withTos = withCards
                .Include(d => d.ExchangesTo)
                    .ThenInclude(ex => ex.Card)
                .Include(d => d.ExchangesTo)
                    .ThenInclude(ex => ex.From!.Owner);

            var withOrderedTos = withTos
                .Include(d => d.ExchangesTo
                    .OrderBy(ex => ex.From!.Owner.Name)
                        .ThenBy(ex => ex.Card.Name));

            return withOrderedTos
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private IEnumerable<RequestNameGroup> DeckNameGroups(Deck deck)
        {
            var amountsByName = deck.Cards
                .ToLookup(ca => ca.Card.Name);

            var requestsByName = deck.ExchangesTo
                .Where(ex => !ex.IsTrade)
                .ToLookup(ex => ex.Card.Name);

            var cardNames = amountsByName
                .Select(g => g.Key)
                .Union(requestsByName
                    .Select(g => g.Key))
                .OrderBy(cn => cn);

            return cardNames.Select(cn =>
                new RequestNameGroup(amountsByName[cn], requestsByName[cn]));
        }



        public async Task<IActionResult> OnPostAsync(int deckId)
        {
            if (deckId == default)
            {
                PostMessage = "Deck is not valid";
                return RedirectToPage("./Index");
            }

            var userId = _userManager.GetUserId(User);

            // keep eye on, could possibly remove trades not started by the user
            // makes the assumption that trades are always started by the owner of the To deck
            var deck = await _dbContext.Decks
                .Include(d => d.ExchangesTo
                    .Where(ex => ex.IsTrade))
                .SingleOrDefaultAsync(d =>
                    d.Id == deckId && d.OwnerId == userId);

            if (deck == default)
            {
                PostMessage = "Deck is not valid";
                return RedirectToPage("./Index");
            }

            if (!deck.ExchangesTo.Any(ex => ex.IsTrade))
            {
                PostMessage = "No trades were found";
                return RedirectToPage("./Index");
            }

            _dbContext.Exchanges.RemoveRange(deck.ExchangesTo);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully cancelled requests";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while cancelling";
            }

            return RedirectToPage("./Index");
        }
    }
}