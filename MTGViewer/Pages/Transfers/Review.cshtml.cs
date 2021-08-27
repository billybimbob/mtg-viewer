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
            CardAmount? ToRequest,
            CardAmount FromAmount,
            CardAmount? FromRequest) { }


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

        // [BindProperty]
        // public IReadOnlyList<string> TradeTokens { get; set; }


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
                .Include(t => t.To)
                .OrderBy(t => t.Card.Name)
                .ToListAsync();

            // var tokens = deckTrades
            //     .Select(_dbContext.GetToken)
            //     .Select(o => o?.ToString())
            //     .ToList();

            if (!deckTrades.Any())
            {
                PostMessage = "No trades were found";
                return RedirectToPage("./Index");
            }

            Source = deck;
            Receiver = deck.Owner;
            Trades = deckTrades;
            // TradeTokens = tokens;

            return Page();
        }



        public async Task<IActionResult> OnPostAcceptAsync(int deckId, int tradeId)
        {
            var acceptInfo = await GetAcceptInfoAsync(tradeId);

            if (acceptInfo == null)
            {
                return RedirectToPage("./Review", new { deckId });
            }

            ApplyAccept(acceptInfo);
            await CascadeIfComplete(acceptInfo);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Trade successfully Applied";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while Accepting";
            }

            return RedirectToPage("./Review");
        }



        private async Task<AcceptAmounts?> GetAcceptInfoAsync(int tradeId)
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
                .SingleOrDefaultAsync(t => t.Id == tradeId && t.From.OwnerId == userId);

            if (deckTrade == default)
            {
                PostMessage = "Trade could not be found";
                return null;
            }

            var tradeDeckIds = new []{ deckTrade.ToId, deckTrade.FromId };
            var tradeAmounts = await _dbContext.Amounts
                .Where(ca =>
                    ca.CardId == deckTrade.CardId && tradeDeckIds.Contains(ca.LocationId))
                .ToListAsync();

            var amountsValid = tradeAmounts.Any(ca =>
                !ca.IsRequest && ca.LocationId == deckTrade.FromId);

            if (!amountsValid)
            {
                PostMessage = "Source Deck lacks the required amount to complete the trade";
                return null;
            }

            return new AcceptAmounts (
                Accept: deckTrade,

                ToAmount: tradeAmounts
                    .SingleOrDefault(ca =>
                        !ca.IsRequest && ca.LocationId == deckTrade.ToId),

                ToRequest: tradeAmounts
                    .SingleOrDefault(ca =>
                        ca.IsRequest && ca.LocationId == deckTrade.ToId),

                FromAmount: tradeAmounts
                    .Single(ca =>
                        !ca.IsRequest && ca.LocationId == deckTrade.FromId),

                FromRequest: tradeAmounts
                    .SingleOrDefault(ca =>
                        ca.IsRequest && ca.LocationId == deckTrade.FromId)
            );
        }


        private void ApplyAccept(AcceptAmounts acceptInfo)
        {
            var (accept, toAmount, toRequest, fromAmount, fromRequest) = acceptInfo;

            if (toRequest is not null)
            {
                if (toAmount == default)
                {
                    toAmount = new CardAmount
                    {
                        Card = accept.Card,
                        Location = accept.To,
                        Amount = 0
                    };

                    _dbContext.Amounts.Add(toAmount);
                }

                if (fromRequest == default)
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

                var changeOptions = new [] { accept.Amount, fromAmount.Amount, toRequest.Amount };
                var change = changeOptions.Min();

                toAmount.Amount += change;
                toRequest.Amount -= change;
                fromAmount.Amount -= change;
                fromRequest.Amount += change;
            }

            if (toRequest?.Amount == 0)
            {
                _dbContext.Amounts.Remove(toRequest);
            }

            if (fromAmount.Amount == 0)
            {
                _dbContext.Amounts.Remove(fromAmount);
            }

            _dbContext.Trades.Remove(accept);
        }


        private async Task CascadeIfComplete(AcceptAmounts acceptInfo)
        {
            var request = acceptInfo.ToRequest;

            if (request?.Amount > 0)
            {
                return;
            }

            var accepted = acceptInfo.Accept;

            var remainingTrades = await _dbContext.Trades
                .Where(t => t.Id != accepted.Id 
                    && t.ProposerId == accepted.ProposerId
                    && t.CardId == accepted.CardId)
                .ToListAsync();

            _dbContext.Trades.RemoveRange(remainingTrades);
        }



        public async Task<IActionResult> OnPostRejectAsync(int deckId, int tradeId)
        {
            if (tradeId == default)
            {
                PostMessage = "Card was not specified";
                return RedirectToPage("./Review", new { deckId });
            }

            var userId = _userManager.GetUserId(User);
            var deckTrade = await _dbContext.Trades
                .SingleOrDefaultAsync(t => t.Id == tradeId && t.From.OwnerId == userId);

            if (deckTrade == default)
            {
                PostMessage = "Trade could not be found";
                return RedirectToPage("./Review", new { deckId });
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

            return RedirectToPage("./Review", new { deckId });
        }
    }
}