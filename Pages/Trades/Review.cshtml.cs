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

        public Location Deck { get; private set; }
        public IReadOnlyList<Trade> ToDeck { get; private set; }
        public IReadOnlyList<Trade> FromDeck { get; private set; }

        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(deckId))
                .Include(t => t.Card)
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
                .AsNoTracking()
                .ToListAsync();

            if (!deckTrades.Any())
            {
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);

            if (!deckTrades.Any(t => 
                t.To.OwnerId == userId || t.From.Location.OwnerId == userId))
            {
                return NotFound();
            }

            Deck = await _dbContext.Locations.FindAsync(deckId);

            if (Deck == null)
            {
                return NotFound();
            }

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


        public async Task<IActionResult> OnPostAcceptAsync(int deckId)
        {
            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(deckId))
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
                .ToArrayAsync();

            var userId = _userManager.GetUserId(User);

            if (!deckTrades.Any(t => 
                t.To.OwnerId == userId || t.From.Location.OwnerId == userId))
            {
                return NotFound();
            }

            var tradesValid = deckTrades.All(t => t.From.Amount <= t.Amount);

            if (!tradesValid)
            {
                PostMessage = "Source Deck lacks the trade amount to complete the trade";
                return NotFound(); // TODO: change to better result
            }

            foreach (var trade in deckTrades)
            {
                await ApplyAcceptAsync(trade);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Trade Successfully Applied";
            }
            catch(DbUpdateConcurrencyException)
            {
                PostMessage = "Ran Into Error while Accepting";
            }
            catch(DbUpdateException)
            {
                PostMessage = "Ran Into Error while Accepting";
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


        public async Task<IActionResult> OnPostRejectAsync(int deckId)
        {
            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(deckId))
                .Include(t => t.To)
                .Include(t => t.From)
                    .ThenInclude(ca => ca.Location)
                .ToArrayAsync();

            var userId = _userManager.GetUserId(User);

            if (!deckTrades.Any(t => 
                t.To.OwnerId == userId || t.From.Location.OwnerId == userId))
            {
                return NotFound();
            }

            _dbContext.RemoveRange(deckTrades);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully Rejected Trades";
            }
            catch(DbUpdateConcurrencyException)
            {
                PostMessage = "Ran Into Error while Rejecting";
            }
            catch(DbUpdateException)
            {
                PostMessage = "Ran Into Error while Rejecting";
            }

            return RedirectToPage("./Index");
        }
    }
}