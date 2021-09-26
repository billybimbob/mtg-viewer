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

        public IReadOnlyList<Exchange>? Trades { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            if (deckId == default)
            {
                return NotFound();
            }

            var deck = await DeckWithCardsAndFromTrades(deckId)
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (!deck.ExchangesFrom.Any())
            {
                return RedirectToPage("./Index");
            }

            Source = deck;
            Receiver = deck.Owner;
            Trades = CappedFromTrades(deck);

            return Page();
        }


        private IQueryable<Deck> DeckWithCardsAndFromTrades(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.Owner)
                .Include(d => d.Cards)

                .Include(d => d.ExchangesFrom)
                    .ThenInclude(ex => ex.Card)

                .Include(d => d.ExchangesFrom)
                    .ThenInclude(ex => ex.To!.Owner)

                .Include(d => d.ExchangesFrom
                    .Where(ex => ex.IsTrade)
                    .OrderBy(ex => ex.Card.Name))

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private IReadOnlyList<Exchange> CappedFromTrades(Deck deck)
        {
            var trades = deck.ExchangesFrom
                .Where(ex => ex.IsTrade)
                .ToList();

            var tradesWithAmountCap = trades
                .Join( deck.Cards,
                    ex => ex.CardId,
                    ca => ca.CardId,
                    (trade, actual) => (trade, actual.Amount));

            foreach (var (trade, cap) in tradesWithAmountCap)
            {
                // modifications are not saved
                trade.Amount = Math.Min(trade.Amount, cap);
            }

            return trades;
        }



        private record AcceptRequest(
            Exchange Trade,
            ExchangeNameGroup? ToRequests,
            CardAmount FromAmount) { }

        private record AcceptTargets(
            CardAmount ToAmount,
            Exchange FromRequest) { }


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


        private async Task<Exchange?> GetTradeAsync(int tradeId)
        {
            if (tradeId == default)
            {
                return null;
            }

            var tradeCard = await _dbContext.Exchanges
                .Where(ex => ex.IsTrade && ex.Id == tradeId)
                .Select(ex => ex.Card)
                .SingleOrDefaultAsync();

            if (tradeCard == default)
            {
                return null;
            }

            return await TradeWithTargets(tradeId, tradeCard).SingleOrDefaultAsync();
        }


        private IQueryable<Exchange> TradeWithTargets(int tradeId, Card tradeCard)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Exchanges
                .Where(t => t.IsTrade
                    && t.Id == tradeId
                    && t.To != default
                    && t.From != default && t.From!.OwnerId == userId)

                .Include(t => t.To)
                    .ThenInclude(d => d!.Cards
                        .Where(ca => ca.CardId == tradeCard.Id))
                        .ThenInclude(ca => ca.Card)

                .Include(t => t.To)
                    .ThenInclude(d => d!.ExchangesTo
                        .Where(ex => !ex.IsTrade
                            && ex.Card.Name == tradeCard.Name))
                        .ThenInclude(ex => ex.Card)

                .Include(t => t.From)
                    .ThenInclude(d => d!.Cards
                        .Where(ca => ca.CardId == tradeCard.Id))
                        .ThenInclude(ex => ex.Card)

                .Include(t => t.From)
                    .ThenInclude(d => d!.ExchangesTo
                        .Where(ex => !ex.IsTrade
                            && ex.CardId == tradeCard.Id))
                        .ThenInclude(ex => ex.Card)

                .AsSplitQuery();
        }



        private AcceptRequest? GetAcceptRequest(Exchange trade)
        {
            if (!trade.IsTrade)
            {
                return null;
            }

            var tradeValid = trade.From?.Cards
                .Select(ca => ca.CardId)
                .Contains(trade.CardId)
                ?? false;

            if (!tradeValid)
            {
                return null;
            }

            var toRequests = trade.To!.ExchangesTo
                .Where(ex => !ex.IsTrade && ex.Card.Name == trade.Card.Name);

            return new AcceptRequest(
                Trade: trade,

                ToRequests: toRequests.Any()
                    ? new ExchangeNameGroup(toRequests) : default,

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
                ?? Enumerable.Empty<Exchange>();

            _dbContext.Exchanges.RemoveRange(finishedRequests);
            _dbContext.Exchanges.Remove(trade);
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

            var fromRequest = trade.From!.ExchangesTo
                .SingleOrDefault(ex => ex.CardId == trade.CardId);

            if (fromRequest is null)
            {
                fromRequest = new()
                {
                    Card = trade.Card,
                    To = trade.From,
                    Amount = 0
                };

                _dbContext.Exchanges.Attach(fromRequest);
            }

            return new AcceptTargets(toAmount, fromRequest);
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

            var remainingTrades = await _dbContext.Exchanges
                .Where(ex => ex.IsTrade
                    && ex.Id != trade.Id
                    && ex.ToId == trade.ToId
                    && ex.Card.Name == trade.Card.Name)
                .ToListAsync();

            if (!remainingTrades.Any())
            {
                return;
            }

            if (toRequests.Amount == 0)
            {
                _dbContext.Exchanges.RemoveRange(remainingTrades);
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

            var deckTrade = await _dbContext.Exchanges
                .SingleOrDefaultAsync(ex => ex.IsTrade
                    && ex.Id == tradeId
                    && ex.From != default
                    && ex.From!.OwnerId == userId);

            if (deckTrade == default)
            {
                PostMessage = "Trade could not be found";
                return RedirectToPage("./Index");
            }

            _dbContext.Exchanges.Remove(deckTrade);

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