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
        public IReadOnlyList<RequestNameGroup>? Cards { get; private set; }
        public IReadOnlyList<Trade>? Trades { get; private set; }


        public async Task<IActionResult> OnGetAsync(int id)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await _dbContext.Decks
                .Include(l => l.Cards
                    .Where(da => da.Intent != Intent.Return)
                    .OrderBy(da => da.Card.Name))
                    .ThenInclude(da => da.Card)
                .FirstOrDefaultAsync(l =>
                    l.Id == id && l.OwnerId == userId);

            if (deck == default)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(t => t.ToId == id || t.FromId == id)
                .Include(t => t.Card)
                .Include(t => t.Proposer)
                .Include(t => t.Receiver)
                .Include(t => t.To)
                .Include(t => t.From)
                .OrderBy(t => t.Card.Name)
                .ToListAsync();

            Deck = deck;
            Cards = deck.Cards
                .GroupBy(ca => ca.Card.Name,
                    (_, amounts) => new RequestNameGroup(amounts))
                .ToList();

            Trades = deckTrades;

            return Page();
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
                .Where(da => da.Intent is Intent.None)
                .Select(da => (da.Card, da.Amount))
                .ToList();

            _dbContext.DeckAmounts.RemoveRange(deck.Cards);
            _dbContext.Decks.Remove(deck);

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
