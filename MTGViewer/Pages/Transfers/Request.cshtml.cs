using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Transfers
{
    [Authorize]
    public class RequestModel : PageModel
    {
        private CardDbContext _dbContext;
        private UserManager<CardUser> _userManager;
        private ILogger<RequestModel> _logger;

        public RequestModel(
            CardDbContext dbContext, UserManager<CardUser> userManager, ILogger<RequestModel> logger)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _logger = logger;
        }


        [TempData]
        public string PostMessage { get; set; }

        public bool TargetsExist { get; private set; }

        public Deck Deck { get; private set; }

        public IReadOnlyList<CardAmount> Requests { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var userId = _userManager.GetUserId(User);
            var deck = await _dbContext.Decks
                .SingleOrDefaultAsync(d => d.Id == deckId && d.OwnerId == userId);

            if (deck == default)
            {
                return NotFound();
            }

            var cardRequests = await _dbContext.Amounts
                .Include(ca => ca.Card)
                .Where(ca => ca.IsRequest && ca.LocationId == deckId)
                .ToListAsync();

            var alreadyRequested = await _dbContext.Trades
                .Where(t => t.ToId == deckId && t.ProposerId == userId)
                .AnyAsync();


            if (!cardRequests.Any())
            {
                PostMessage = "There are no possible requests";
                return RedirectToPage("./Index");
            }

            if (cardRequests.Any() && alreadyRequested)
            {
                return RedirectToPage("./Status", new { deckId });
            }

            TargetsExist = await AnyTradeRequestsAsync(userId, deckId);
            Deck = deck;
            Requests = cardRequests;

            return Page();
        }


        private async Task<bool> AnyTradeRequestsAsync(string userId, int deckId)
        {
            var possibleAmounts = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != userId);

            var cardRequests = _dbContext.Amounts
                .Where(ca => ca.IsRequest && ca.LocationId == deckId);

            return await possibleAmounts
                .Join( cardRequests,
                    amount => amount.CardId,
                    request => request.CardId,
                    (amount, request) => amount)
                .AnyAsync();
        }


        public async Task<IActionResult> OnPostAsync(int deckId)
        {
            var userId = _userManager.GetUserId(User);
            var user = await _dbContext.Users.FindAsync(userId);

            var validDeck = await _dbContext.Decks
                .AnyAsync(d => d.Id == deckId && d.OwnerId == user.Id);

            if (!validDeck)
            {
                return NotFound();
            }

            var alreadyRequested = await _dbContext.Trades
                .Where(t => t.ToId == deckId && t.ProposerId == user.Id)
                .AnyAsync();

            if (alreadyRequested)
            {
                PostMessage = "Request is already sent";
                return RedirectToPage("./Index");
            }

            var tradeRequests = await FindTradeRequestsAsync(user, deckId);

            if (!tradeRequests.Any())
            {
                PostMessage = "There are no possible decks to request to";
                return RedirectToPage("./Index");
            }

            _dbContext.Trades.AttachRange(tradeRequests);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Request was successfully sent";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into issue while trying to send request";
            }

            return RedirectToPage("./Index");
        }


        private async Task<IReadOnlyList<Trade>> FindTradeRequestsAsync(UserRef user, int deckId)
        {
            var possibleAmounts = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != user.Id)
                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                    .ThenInclude(l => (l as Deck).Owner);

            var cardRequests = _dbContext.Amounts
                .Where(ca => ca.IsRequest && ca.LocationId == deckId)
                .Include(ca => ca.Card)
                .Include(ca => ca.Location);

            var requestTargets = await possibleAmounts
                .Join( cardRequests,
                    amount => amount.CardId,
                    request => request.CardId,
                    (amount, request) => new { amount, request })
                .ToListAsync();


            var newTrades = requestTargets.Select(ar =>
            {
                var toDeck = (Deck) ar.request.Location;
                var fromDeck = (Deck) ar.amount.Location;
                return new Trade
                {
                    Card = ar.request.Card,
                    Proposer = user,
                    Receiver = fromDeck.Owner,
                    From = fromDeck,
                    To = toDeck,
                    // TODO: change how to split amounts
                    Amount = Math.Min(ar.request.Amount, ar.amount.Amount)
                };
            });
            
            return newTrades.ToList();
        }
    }
}