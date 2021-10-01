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
        public IReadOnlyList<AmountRequestNameGroup>? NameGroups { get; private set; }

        public IReadOnlyList<Trade>? Trades { get; private set; }


        public async Task<IActionResult> OnGetAsync(int id)
        {
            var deck = await DeckForDelete(id).FirstOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            var deckTrades = deck.GetTrades()
                .OrderBy(t => t.Card.Name)
                .ToList();

            Deck = deck;

            NameGroups = DeckNameGroup(deck).ToList();

            Trades = deckTrades;

            return Page();
        }


        private IQueryable<Deck> DeckForDelete(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.Cards)
                    .ThenInclude(da => da.Card)

                .Include(d => d.Requests)
                    .ThenInclude(cr => cr.Card)
                .Include(d => d.Requests)
                    .ThenInclude(cr => cr.Target)

                .Include(d => d.TradesTo)
                    .ThenInclude(t => t.Card)
                .Include(d => d.TradesTo)
                    .ThenInclude(t => t.From)

                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.Card)
                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.To)

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private IEnumerable<AmountRequestNameGroup> DeckNameGroup(Deck deck)
        {
            var amountsByName = deck.Cards
                .ToLookup(ca => ca.Card.Name);

            var requestsByName = deck.Requests
                .ToLookup(ex => ex.Card.Name);

            var cardNames = amountsByName
                .Select(g => g.Key)
                .Union(requestsByName.Select(g => g.Key))
                .OrderBy(cn => cn);

            return cardNames.Select(cn =>
                new AmountRequestNameGroup(amountsByName[cn], requestsByName[cn]));
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
                // no source, since deck is being deleted
                .Select(da => new CardReturn(da.Card, da.Amount))
                .ToList();

            _dbContext.Amounts.RemoveRange(deck.Cards);
            _dbContext.Decks.Remove(deck);

            try
            {
                if (returningCards.Any())
                {
                    await _sharedStorage.ReturnAsync(returningCards);
                }

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
