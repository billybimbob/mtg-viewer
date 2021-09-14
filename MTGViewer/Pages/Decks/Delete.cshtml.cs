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
        public IReadOnlyList<SameNamePair>? Cards { get; private set; }
        public IReadOnlyList<Trade>? Trades { get; private set; }


        public async Task<IActionResult> OnGetAsync(int id)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await _dbContext.Decks
                .Include(l => l.Cards
                    .OrderBy(ca => ca.Card.Name))
                    .ThenInclude(ca => ca.Card)
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
                    (_, amounts) => new SameNamePair(amounts))
                .ToList();

            Trades = deckTrades;

            return Page();
        }



        public async Task<IActionResult> OnPostAsync(int id)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await _dbContext.Decks
                .Include(ca => ca.Cards)
                    .ThenInclude(ca => ca.Card)
                .FirstOrDefaultAsync(l =>
                    l.Id == id && l.OwnerId == userId);

            if (deck == default)
            {
                return RedirectToPage("./Index");
            }

            // var returned = await ReturnCardsAsync(deck);

            // if (!returned)
            // {
            //     PostMesssage = "Failed to return all cards";
            //     return RedirectToPage("./Index");
            // }

            var returningCards = deck.Cards
                .Where(ca => !ca.IsRequest)
                .Select(ca => (ca.Card, ca.Amount))
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


        // private async Task<bool> ReturnCardsAsync(Deck deck)
        // {
        //     var deckAmounts = _dbContext.Amounts
        //         .Where(ca => ca.LocationId == deck.Id);

        //     var sharedAmounts = _dbContext.Amounts
        //         .Where(ca => ca.Location is Data.Shared);

        //     var amountPairs = await deckAmounts
        //         .GroupJoin( sharedAmounts,
        //             deck => deck.CardId,
        //             shared => shared.CardId,
        //             (deck, shares) => new { deck, shares })
        //         .SelectMany(
        //             dss => dss.shares.DefaultIfEmpty(),
        //             (dss, shared) => new { dss.deck, shared })
        //         .ToListAsync();


        //     var noMatch = amountPairs.Any(ds => ds.shared == default);

        //     if (noMatch)
        //     {
        //         return false;
        //     }

        //     foreach(var pair in amountPairs)
        //     {
        //         if (!pair.deck.IsRequest)
        //         {
        //             pair.shared!.Amount += pair.deck.Amount;
        //         }
        //     }

        //     return true;
        // }
    }
}
