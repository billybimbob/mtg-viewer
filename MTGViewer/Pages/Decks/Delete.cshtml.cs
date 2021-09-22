using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

#nullable enable

namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class DeleteModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;
        private readonly ISharedStorage _sharedStorage;
        private readonly ILogger<DeleteModel> _logger;

        public DeleteModel(
            UserManager<CardUser> userManager,
            CardDbContext dbContext,
            ISharedStorage sharedStorage,
            ILogger<DeleteModel> logger)
        {
            _userManager = userManager;
            _dbContext = dbContext;
            _sharedStorage = sharedStorage;
            _logger = logger;
        }


        [TempData]
        public string? PostMesssage { get; set; }

        public Deck? Deck { get; private set; }
        public IReadOnlyList<RequestNameGroup>? NameGroups { get; private set; }

        public IReadOnlyList<Exchange>? Trades { get; private set; }


        public async Task<IActionResult> OnGetAsync(int id)
        {
            var deck = await DeckWithExchanges(id).FirstOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var deckTrades = deck.GetAllExchanges()
                .Where(ex => ex.IsTrade)
                .OrderBy(ex => ex.Card.Name)
                .ToList();

            Deck = deck;

            NameGroups = DeckNameGroup(deck).ToList();

            Trades = deckTrades;

            return Page();
        }


        private IQueryable<Deck> DeckWithExchanges(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            var deck = _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId);

            var withCards = deck
                .Include(d => d.Cards)
                    .ThenInclude(da => da.Card);

            var withToTrades = withCards
                .Include(d => d.ExchangesTo)
                    .ThenInclude(ex => ex.Card)
                .Include(d => d.ExchangesTo)
                    .ThenInclude(ex => ex.From);

            var withFromTrades = withToTrades
                .Include(d => d.ExchangesFrom)
                    .ThenInclude(ex => ex.Card)
                .Include(d => d.ExchangesFrom)
                    .ThenInclude(ex => ex.To);

            return withFromTrades
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private IEnumerable<RequestNameGroup> DeckNameGroup(Deck deck)
        {
            var amountsByName = deck.Cards
                .ToLookup(ca => ca.Card.Name);

            var requestsByName = deck.GetAllExchanges()
                .Where(ex => !ex.IsTrade)
                .ToLookup(ex => ex.Card.Name);

            var cardNames = amountsByName
                .Select(g => g.Key)
                .Union(requestsByName.Select(g => g.Key))
                .OrderBy(cn => cn);

            return cardNames.Select(cn =>
                new RequestNameGroup(amountsByName[cn], requestsByName[cn]));
        }



        public async Task<IActionResult> OnPostAsync(int id)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await _dbContext.Decks
                .Include(d => d.Cards)
                    .ThenInclude(da => da.Card)
                .FirstOrDefaultAsync(d =>
                    d.Id == id && d.OwnerId == userId);

            if (deck == default)
            {
                return RedirectToPage("./Index");
            }

            var returningCards = deck.Cards
                .Select(da => (da.Card, da.Amount))
                .ToList();

            _dbContext.Amounts.RemoveRange(deck.Cards);
            _dbContext.Decks.Remove(deck);

            // keep eye on, possibly remove exchanges and suggestions

            try
            {
                await _sharedStorage.ReturnAsync(returningCards);
                await _dbContext.SaveChangesAsync();

                PostMesssage = $"Successfully deleted {deck.Name}";
            }
            catch (DbUpdateException)
            {
                PostMesssage = $"Ran into issue while trying to delete {deck.Name}";
            }

            return RedirectToPage("./Index");
        }
    }
}
