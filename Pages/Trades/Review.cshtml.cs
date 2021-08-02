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


namespace MTGViewer.Pages.Trades
{
    [Authorize]
    public class ReviewModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        public ReviewModel(CardDbContext dbContext, UserManager<CardUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }


        [TempData]
        public string PostMessage { get; set; }

        public CardUser Proposer { get; private set; }
        public Location Deck { get; private set; }

        public IReadOnlyList<Trade> ToDeck { get; private set; }
        public IReadOnlyList<Trade> FromDeck { get; private set; }


        public async Task<IActionResult> OnGetAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Locations.FindAsync(deckId);

            if (deck == null || deck.OwnerId == proposerId)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(proposerId, deckId))
                .Include(t => t.Card)
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
                .AsNoTracking()
                .ToListAsync();

            if (!deckTrades.Any())
            {
                PostMessage = "Cannot find any trades";
                return RedirectToPage("./Index");
            }

            var userId = _userManager.GetUserId(User);

            if (deckTrades.Any(t => 
                t.To.OwnerId != userId && t.From.Location.OwnerId != userId))
            {
                return NotFound();
            }

            if (!deckTrades.All(t =>
                t.ReceiverId == userId && !t.IsCounter
                    || t.ProposerId == userId && t.IsCounter))
            {
                return NotFound();
            }

            await _dbContext.Entry(deck)
                .Reference(l => l.Owner)
                .LoadAsync();

            Deck = deck;

            Proposer = await _userManager.FindByIdAsync(proposerId);

            ToDeck = deckTrades
                .Where(t => t.To.Id == deckId)
                .OrderBy(t => t.Card.Name)
                .ToList();

            FromDeck = deckTrades
                .Except(ToDeck)
                .OrderBy(t => t.Card.Name)
                .ToList();

            return Page();
        }


        public async Task<IActionResult> OnPostAcceptAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Locations.FindAsync(deckId);

            if (deck == null || deck.OwnerId == proposerId)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(proposerId, deckId))
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
                .ToArrayAsync();

            var userId = _userManager.GetUserId(User);

            if (!deckTrades.Any(t => 
                t.To.OwnerId == userId || t.From.Location.OwnerId == userId))
            {
                PostMessage = "Cannot find any trades to Accept";
                return RedirectToPage("./Index");
            }

            var tradesValid = deckTrades.All(t => t.From.Amount <= t.Amount);

            if (!tradesValid)
            {
                PostMessage = "Source Deck lacks the trade amount to complete the trade";
                return RedirectToPage("./Index");
            }

            foreach (var trade in deckTrades)
            {
                await ApplyAcceptAsync(trade);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Trade successfully Applied";
            }
            catch(DbUpdateConcurrencyException)
            {
                PostMessage = "Ran into error while Accepting";
            }
            catch(DbUpdateException)
            {
                PostMessage = "Ran into error while Accepting";
            }

            return RedirectToPage("./Index");
        }


        private async Task ApplyAcceptAsync(Trade accept)
        {
            var destAmount = await _dbContext.Amounts
                .SingleOrDefaultAsync(ca =>
                    ca.CardId == accept.CardId
                        && ca.LocationId == accept.ToId
                        && !ca.IsRequest);

            if (destAmount is null)
            {
                destAmount = new CardAmount
                {
                    CardId = accept.CardId,
                    LocationId = accept.ToId
                };

                _dbContext.Attach(destAmount);
            }

            accept.From.Amount -= accept.Amount;
            destAmount.Amount += accept.Amount;

            _dbContext.Remove(accept);
        }


        public async Task<IActionResult> OnPostRejectAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Locations.FindAsync(deckId);

            if (deck == null || deck.OwnerId == proposerId)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(proposerId, deckId))
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
                .ToArrayAsync();

            var userId = _userManager.GetUserId(User);

            if (!deckTrades.Any(t => 
                t.To.OwnerId == userId || t.From.Location.OwnerId == userId))
            {
                PostMessage = "Cannot find any trades to Reject";
                return RedirectToPage("./Index");
            }

            _dbContext.RemoveRange(deckTrades);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully Rejected Trades";
            }
            catch(DbUpdateConcurrencyException)
            {
                PostMessage = "Ran into error while Rejecting";
            }
            catch(DbUpdateException)
            {
                PostMessage = "Ran into error while Rejecting";
            }

            return RedirectToPage("./Index");
        }
    }
}