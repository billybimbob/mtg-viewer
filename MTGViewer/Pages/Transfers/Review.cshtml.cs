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

            var deck = await DeckWithCardsAndTradesFrom(deckId)
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (!deck.TradesFrom.Any())
            {
                return RedirectToPage("./Index");
            }

            Source = deck;
            Receiver = deck.Owner;
            Trades = CappedFromTrades(deck);

            return Page();
        }


        private IQueryable<Deck> DeckWithCardsAndTradesFrom(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.Owner)
                .Include(d => d.Cards)

                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.Card)
                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.To.Owner)

                .Include(d => d.TradesFrom
                    .OrderBy(t => t.Card.Name))

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private IReadOnlyList<Trade> CappedFromTrades(Deck deck)
        {
            var tradesWithAmountCap = deck.TradesFrom
                .Join( deck.Cards,
                    ex => ex.CardId,
                    ca => ca.CardId,
                    (trade, actual) => (trade, actual.Amount));

            foreach (var (trade, cap) in tradesWithAmountCap)
            {
                // modifications are not saved
                trade.Amount = Math.Min(trade.Amount, cap);
            }

            return deck.TradesFrom.ToList();
        }



        private record AcceptRequest(
            Trade Trade,
            RequestNameGroup? ToTakes,
            CardAmount FromAmount) { }

        private record AcceptTargets(
            CardAmount ToAmount,
            CardRequest FromTake) { }


        public async Task<IActionResult> OnPostAcceptAsync(int tradeId, int amount)
        {
            var trade = await GetTradeAsync(tradeId);

            if (trade == null)
            {
                PostMessage = "Trade could not be found";
                return RedirectToPage("./Index");
            }

            var acceptRequest = GetAcceptRequest(trade);

            if (acceptRequest == null)
            {
                PostMessage = "Source Deck lacks the cards to complete the trade";
                return RedirectToPage("./Review",
                    new { deckId = trade.FromId });
            }

            ApplyAccept(acceptRequest, amount);

            await UpdateRemainingTrades(acceptRequest);

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
                new { deckId = trade.FromId });
        }


        private async Task<Trade?> GetTradeAsync(int tradeId)
        {
            if (tradeId == default)
            {
                return null;
            }

            var tradeCard = await _dbContext.Trades
                .Where(t => t.Id == tradeId)
                .Select(t => t.Card)
                .SingleOrDefaultAsync();

            if (tradeCard == default)
            {
                return null;
            }

            return await TradeWithTargets(tradeId, tradeCard).SingleOrDefaultAsync();
        }


        private IQueryable<Trade> TradeWithTargets(int tradeId, Card tradeCard)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Trades
                .Where(t => t.Id == tradeId && t.From.OwnerId == userId)

                .Include(t => t.To)
                    .ThenInclude(d => d.Cards
                        .Where(ca => ca.CardId == tradeCard.Id))
                        .ThenInclude(ca => ca.Card)

                .Include(t => t.To)
                    .ThenInclude(d => d.Requests
                        .Where(cr => !cr.IsReturn && cr.Card.Name == tradeCard.Name))
                        .ThenInclude(ex => ex.Card)

                .Include(t => t.From)
                    .ThenInclude(d => d.Cards
                        .Where(ca => ca.CardId == tradeCard.Id))
                        .ThenInclude(ex => ex.Card)

                .Include(t => t.From)
                    .ThenInclude(d => d.Requests
                        .Where(cr => !cr.IsReturn && cr.CardId == tradeCard.Id))
                        .ThenInclude(ex => ex.Card)

                .AsSplitQuery();
        }



        private AcceptRequest? GetAcceptRequest(Trade trade)
        {
            var tradeValid = trade.From.Cards
                .Select(ca => ca.CardId)
                .Contains(trade.CardId);

            if (!tradeValid)
            {
                return null;
            }

            var toTakes = trade.To.Requests
                .Where(cr => !cr.IsReturn && cr.Card.Name == trade.Card.Name);

            return new AcceptRequest(
                Trade: trade,

                ToTakes: toTakes.Any()
                    ? new RequestNameGroup(toTakes) : default,

                FromAmount: trade.From!.Cards
                    .Single(ca => ca.CardId == trade.CardId));
        }


        private void ApplyAccept(AcceptRequest acceptRequest, int amount)
        {
            var (trade, toRequests, fromAmount) = acceptRequest;

            ModifyAmountsAndRequests(acceptRequest, amount);

            if (fromAmount.Amount == 0)
            {
                _dbContext.Amounts.Remove(fromAmount);
            }

            var finishedRequests = toRequests
                ?.Where(ex  => ex.Amount == 0)
                ?? Enumerable.Empty<CardRequest>();

            _dbContext.Requests.RemoveRange(finishedRequests);
            _dbContext.Trades.Remove(trade);
        }


        private void ModifyAmountsAndRequests(AcceptRequest acceptRequest, int requestAmount)
        {
            var (trade, toRequests, fromAmount) = acceptRequest;

            if (toRequests == default)
            {
                return;
            }

            var (toAmount, fromRequest) = GetAcceptTargets(acceptRequest);

            var exactRequest = toRequests
                .SingleOrDefault(ex => ex.CardId == trade.CardId);

            int change = new [] {
                requestAmount, trade.Amount,
                toRequests.Amount, fromAmount.Amount }.Min();

            if (exactRequest != default)
            {
                int exactChange = Math.Min(change, exactRequest.Amount);
                int nonExactChange = change - exactChange;

                // exactRequest mod is also reflected in toRequests
                exactRequest.Amount = exactChange;
                toRequests.Amount -= nonExactChange;
            }
            else
            {
                toRequests.Amount -= change;
            }

            toAmount.Amount += change;
            fromAmount.Amount -= change;
            fromRequest.Amount += change;
        }


        private AcceptTargets GetAcceptTargets(AcceptRequest request)
        {
            var trade = request.Trade;

            var toAmount = request.Trade.To!.Cards
                .SingleOrDefault(ca => ca.CardId == trade.CardId);

            if (toAmount is null)
            {
                toAmount = new()
                {
                    Card = trade.Card,
                    Location = trade.To!,
                    Amount = 0
                };

                _dbContext.Amounts.Attach(toAmount);
            }

            var fromTake = trade.From.Requests
                .SingleOrDefault(cr => 
                    !cr.IsReturn && cr.CardId == trade.CardId);

            if (fromTake is null)
            {
                fromTake = new()
                {
                    Card = trade.Card,
                    Target = trade.From,
                    Amount = 0
                };

                _dbContext.Requests.Attach(fromTake);
            }

            return new AcceptTargets(toAmount, fromTake);
        }



        private async Task UpdateRemainingTrades(AcceptRequest acceptRequest)
        {
            var (trade, toRequests, _) = acceptRequest;

            if (toRequests == default)
            {
                return;
            }

            // keep eye on, could possibly remove trades not started
            // by the user
            // current impl makes the assumption that trades are always
            // started by the owner of the To deck

            var remainingTrades = await _dbContext.Trades
                .Where(t => t.Id != trade.Id
                    && t.ToId == trade.ToId
                    && t.Card.Name == trade.Card.Name)
                .ToListAsync();

            if (!remainingTrades.Any())
            {
                return;
            }

            if (toRequests.Amount == 0)
            {
                _dbContext.Trades.RemoveRange(remainingTrades);
                return;
            }

            foreach (var remaining in remainingTrades)
            {
                remaining.Amount = toRequests.Amount;
            }
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
                .SingleOrDefaultAsync(t => t.Id == tradeId
                    && t.From != default
                    && t.From!.OwnerId == userId);

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