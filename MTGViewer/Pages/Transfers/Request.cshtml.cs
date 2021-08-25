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


        public Deck Deck { get; private set; }
        public CardUser Proposer { get; private set; }
        public IReadOnlyList<CardAmount> Requests { get; private set; }



        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var user = await _userManager.GetUserAsync(User);
            var deck = await _dbContext.Decks
                .SingleOrDefaultAsync(d => d.Id == deckId && d.OwnerId == user.Id);

            if (deck == default)
            {
                return NotFound();
            }

            var isDeckValid = await _dbContext.Trades
                .Where(t => t.ToId == deckId && t.ProposerId == user.Id)
                .AnyAsync();

            if (!isDeckValid)
            {
                return NotFound();
            }

            var cardRequests = await _dbContext.Amounts
                .Where(ca => ca.IsRequest && ca.LocationId == deckId)
                .ToListAsync();

            if (!cardRequests.Any())
            {
                return NotFound();
            }

            Deck = deck;
            Proposer = user;
            Requests = cardRequests;

            return Page();
        }


        public async Task<bool> IsDeckValid(int deckId, string userId)
        {
            var validDeck = await _dbContext.Decks
                .AnyAsync(d => d.Id == deckId && d.OwnerId == userId);

            if (!validDeck)
            {
                return false;
            }

            var alreadyRequested = await _dbContext.Trades
                .Where(t => t.ToId == deckId && t.ProposerId == userId)
                .AnyAsync();

            return !alreadyRequested;
        }


        public async Task<IActionResult> OnPostAsync(int deckId)
        {
            var user = await _userManager.GetUserAsync(User);
            var isDeckValid = await IsDeckValid(deckId, user.Id);

            if (!isDeckValid)
            {
                return NotFound();
            }

            var tradeRequests = await FindTradeRequestsAsync(user, deckId);

            if (!tradeRequests.Any())
            {
                return NotFound();
            }

            _dbContext.Trades.AttachRange(tradeRequests);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                _logger.LogError($"ran into error {e}");
            }

            return RedirectToPage("./Index");
        }


        private async Task<IReadOnlyList<Trade>> FindTradeRequestsAsync(CardUser user, int deckId)
        {
            var possibleAmounts = _dbContext.Amounts
                .Include(ca => ca.Card)
                .Include(ca => ca.Location as Deck)
                    .ThenInclude(d => d.Owner)
                .Where(ca => !ca.IsRequest
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != user.Id);

            var cardRequests = _dbContext.Amounts
                .Where(ca => ca.IsRequest && ca.LocationId == deckId);


            var requestTargets = await possibleAmounts
                .GroupJoin( cardRequests,
                    amount => amount.CardId,
                    request => request.CardId,
                    (amount, requests) => new { amount, requests })
                .SelectMany(
                    ars => ars.requests.DefaultIfEmpty(),
                    (ars, request) => new { ars.amount, request })
                .ToListAsync();


            var newTrades =  requestTargets.Select(ar =>
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