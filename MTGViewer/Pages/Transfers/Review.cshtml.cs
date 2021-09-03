using System;
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
        private record AcceptAmounts(
            Trade Accept,
            CardAmount? ToAmount,
            NameGroup? ToRequests,
            CardAmount FromAmount,
            CardAmount? FromRequest)
        {
            public static AcceptAmounts FromTrade(Trade trade, IEnumerable<CardAmount> amounts)
            {
                var toRequests = amounts
                    .Where(ca => ca.IsRequest && ca.LocationId == trade.ToId);

                return new AcceptAmounts(
                    trade,

                    amounts.SingleOrDefault(ca =>
                        !ca.IsRequest && ca.LocationId == trade.ToId),

                    toRequests.Any() ? new NameGroup(toRequests) : null,

                    amounts.Single(ca =>
                        !ca.IsRequest && ca.LocationId == trade.FromId),

                    amounts.SingleOrDefault(ca =>
                        ca.IsRequest && ca.LocationId == trade.FromId)
                );
            }
        }


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



        public async Task<IActionResult> OnPostAcceptAsync(int tradeId)
        {
            var deckTrade = await GetDeckTradeAsync(tradeId);

            if (deckTrade == null)
            {
                return RedirectToPage("./Index");
            }

            var acceptAmounts = await GetAcceptAmountsAsync(deckTrade);

            if (acceptAmounts == null)
            {
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
                PostMessage = "No card was specified";
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
                PostMessage = "Trade could not be found";
                return null;
            }

            return deckTrade;
        }


        private async Task<AcceptAmounts?> GetAcceptAmountsAsync(Trade trade)
        {
            var tradeAmounts = await _dbContext.Amounts
                .Where(ca =>
                    ca.IsRequest
                        && ca.LocationId == trade.ToId 
                        && ca.Card.Name == trade.Card.Name
                    || !ca.IsRequest
                        && ca.LocationId == trade.ToId
                        && ca.CardId == trade.CardId
                    || ca.LocationId == trade.FromId
                        && ca.CardId == trade.CardId)
                .ToListAsync();

            var amountsValid = tradeAmounts.Any(ca =>
                !ca.IsRequest && ca.LocationId == trade.FromId);

            if (!amountsValid)
            {
                PostMessage = "Source Deck lacks the required amount to complete the trade";
                return null;
            }

            return AcceptAmounts.FromTrade(trade, tradeAmounts);
        }


        private void ApplyAccept(AcceptAmounts acceptInfo)
        {
            var (accept, toAmount, toRequests, fromAmount, fromRequest) = acceptInfo;

            if (toRequests is not null)
            {
                if (toAmount is null)
                {
                    toAmount = new CardAmount
                    {
                        Card = accept.Card,
                        Location = accept.To,
                        Amount = 0
                    };

                    _dbContext.Amounts.Add(toAmount);
                }

                if (fromRequest is null)
                {
                    fromRequest = new CardAmount
                    {
                        Card = accept.Card,
                        Location = accept.From,
                        Amount = 0,
                        IsRequest = true
                    };

                    _dbContext.Amounts.Add(fromRequest);
                }

                var changeOptions = new [] { accept.Amount, fromAmount.Amount, toRequests.Amount };
                var change = changeOptions.Min();

                // TODO: prioritize change to exact request amount
                toAmount.Amount += change;
                toRequests.Amount -= change;
                fromAmount.Amount -= change;
                fromRequest.Amount += change;
            }

            var finishedRequests = toRequests?.Where(ca  => ca.Amount == 0);

            if (finishedRequests?.Any() ?? false)
            {
                _dbContext.Amounts.RemoveRange(finishedRequests);
            }

            if (fromAmount.Amount == 0)
            {
                _dbContext.Amounts.Remove(fromAmount);
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