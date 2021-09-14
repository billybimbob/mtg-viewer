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
        public string? PostMessage { get; set; }

        public Deck? Source { get; set; }
        public UserRef? Receiver { get; private set; }

        public IReadOnlyList<Trade>? Trades { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            if (deckId == default)
            {
                return NotFound();
            }

            var userId = _userManager.GetUserId(User);
            
            var deck = await _dbContext.Decks
                .Include(d => d.Owner)
                .SingleOrDefaultAsync(d =>
                    d.Id == deckId && d.OwnerId == userId);

            if (deck == default)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(t => t.ReceiverId == userId && t.FromId == deckId)
                .Include(t => t.Card)
                .Include(t => t.Proposer)
                .Include(t => t.To)
                .OrderBy(t => t.Card.Name)
                .ToListAsync();

            if (!deckTrades.Any())
            {
                return RedirectToPage("./Index");
            }

            Source = deck;
            Receiver = deck.Owner;
            Trades = deckTrades;

            return Page();
        }



        private class AcceptAmounts
        {
            public Trade Accept { get; }
            public DeckAmount? ToAmount { get; }
            public CardNameGroup? ToRequests { get; }
            public DeckAmount FromAmount { get; }
            public DeckAmount? FromRequest { get; }

            public AcceptAmounts(Trade trade, IEnumerable<DeckAmount> amounts)
            {
                Accept = trade;

                ToAmount = amounts.SingleOrDefault(ca =>
                    !ca.IsRequest && ca.LocationId == trade.ToId);

                var toRequests = amounts
                    .Where(ca => ca.IsRequest && ca.LocationId == trade.ToId);

                ToRequests = toRequests.Any()
                    ? new CardNameGroup(toRequests)
                    : null;

                FromAmount = amounts.Single(ca =>
                    !ca.IsRequest && ca.LocationId == trade.FromId);

                FromRequest = amounts.SingleOrDefault(ca =>
                    ca.IsRequest && ca.LocationId == trade.FromId);
            }
        }


        public async Task<IActionResult> OnPostAcceptAsync(int tradeId)
        {
            var deckTrade = await GetDeckTradeAsync(tradeId);

            if (deckTrade == null)
            {
                PostMessage = "Trade could not be found";
                return RedirectToPage("./Index");
            }

            var acceptAmounts = await GetAcceptAmountsAsync(deckTrade);

            if (acceptAmounts == null)
            {
                PostMessage = "Source Deck lacks the required amount to complete the trade";
                return RedirectToPage("./Review",
                    new { deckId = deckTrade.FromId });
            }

            ApplyAccept(acceptAmounts);
            await CascadeIfComplete(acceptAmounts);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Trade successfully Applied";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while Accepting";
            }

            return RedirectToPage("./Review",
                new { deckId = deckTrade.FromId });
        }


        private async Task<Trade?> GetDeckTradeAsync(int tradeId)
        {
            if (tradeId == default)
            {
                return null;
            }

            var userId = _userManager.GetUserId(User);

            var deckTrade = await _dbContext.Trades
                .Include(t => t.Card)
                .Include(t => t.To)
                .Include(t => t.From)
                .SingleOrDefaultAsync(t =>
                    t.Id == tradeId && t.From.OwnerId == userId);

            if (deckTrade == default)
            {
                return null;
            }

            return deckTrade;
        }


        private async Task<AcceptAmounts?> GetAcceptAmountsAsync(Trade trade)
        {
            // TODO: fix request check
            var tradeAmounts = await _dbContext.DeckAmounts
                .Where(da =>
                    da.RequestType == RequestType.Insert
                        && da.LocationId == trade.ToId 
                        && da.Card.Name == trade.Card.Name
                    || !da.IsRequest
                        && da.LocationId == trade.ToId
                        && da.CardId == trade.CardId
                    || da.LocationId == trade.FromId
                        && da.CardId == trade.CardId)
                .ToListAsync();

            var amountsValid = tradeAmounts.Any(ca =>
                !ca.IsRequest && ca.LocationId == trade.FromId);

            if (!amountsValid)
            {
                return null;
            }

            return new AcceptAmounts(trade, tradeAmounts);
        }


        private void ApplyAccept(AcceptAmounts acceptInfo)
        {
            var accept = acceptInfo.Accept;
            var toRequests = acceptInfo.ToRequests;
            var fromAmount = acceptInfo.FromAmount;

            if (toRequests is not null)
            {
                var toAmount = acceptInfo.ToAmount;
                var fromRequest = acceptInfo.FromRequest;

                if (toAmount is null)
                {
                    toAmount = new()
                    {
                        Card = accept.Card,
                        Location = accept.To,
                        Amount = 0
                    };

                    _dbContext.DeckAmounts.Add(toAmount);
                }

                if (fromRequest is null)
                {
                    fromRequest = new()
                    {
                        Card = accept.Card,
                        Location = accept.From,
                        Amount = 0,
                        RequestType = RequestType.Insert
                    };

                    _dbContext.DeckAmounts.Add(fromRequest);
                }

                var changeOptions = new [] { accept.Amount, fromAmount.Amount, toRequests.Amount };
                var change = changeOptions.Min();

                // TODO: prioritize change to exact request amount
                toAmount.Amount += change;
                toRequests.Amount -= change;
                fromAmount.Amount -= change;
                fromRequest.Amount += change;
            }

            var finishedRequests =
                toRequests?
                    .Where(ca  => ca.Amount == 0)
                    .Cast<DeckAmount>()
                ?? Enumerable.Empty<DeckAmount>();

            if (finishedRequests.Any())
            {
                _dbContext.DeckAmounts.RemoveRange(finishedRequests);
            }

            if (fromAmount.Amount == 0)
            {
                _dbContext.DeckAmounts.Remove(fromAmount);
            }

            _dbContext.Trades.Remove(accept);
        }


        private async Task CascadeIfComplete(AcceptAmounts acceptInfo)
        {
            var requests = acceptInfo.ToRequests;

            if (requests?.Amount > 0)
            {
                return;
            }

            var accepted = acceptInfo.Accept;

            var remainingTrades = await _dbContext.Trades
                .Where(t => t.Id != accepted.Id 
                    && t.ProposerId == accepted.ProposerId
                    && t.ToId == accepted.ToId
                    && t.Card.Name == accepted.Card.Name)
                .ToListAsync();

            _dbContext.Trades.RemoveRange(remainingTrades);
        }



        public async Task<IActionResult> OnPostRejectAsync(int tradeId)
        {
            if (tradeId == default)
            {
                PostMessage = "Card was not specified";
                return RedirectToPage("./Index");
            }

            var userId = _userManager.GetUserId(User);
            var deckTrade = await _dbContext.Trades
                .SingleOrDefaultAsync(t => t.Id == tradeId && t.From.OwnerId == userId);

            if (deckTrade == default)
            {
                PostMessage = "Trade could not be found";
                return RedirectToPage("./Index");
            }

            _dbContext.Trades.Remove(deckTrade);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully rejected Trade";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while rejecting";
            }

            return RedirectToPage("./Review",
                new { deckId = deckTrade.FromId });
        }
    }
}