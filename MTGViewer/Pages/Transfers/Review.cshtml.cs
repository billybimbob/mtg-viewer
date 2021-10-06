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

        [BindProperty]
        public Deck? Deck { get; set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            if (deckId == default)
            {
                return NotFound();
            }

            var deck = await DeckForReview(deckId).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (!deck.TradesFrom.Any())
            {
                return RedirectToPage("./Index");
            }

            CapFromTrades(deck);

            Deck = deck;

            return Page();
        }


        private IQueryable<Deck> DeckForReview(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.Owner)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)

                .Include(d => d.Cards
                    // unbounded: limit
                    .OrderBy(ca => ca.Card.Name))

                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.Card)

                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.To.Owner)

                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.To.Cards)
                        // unbounded: keep eye on
                        .ThenInclude(ca => ca.Card)

                .Include(d => d.TradesFrom)
                    .ThenInclude(t => t.To.Wants)
                        // unbounded: keep eye on
                        .ThenInclude(ca => ca.Card)

                .Include(d => d.TradesFrom
                    // unbounded: limit
                    .OrderBy(t => t.To.Owner.Name)
                        .ThenBy(t => t.Card.Name))

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private void CapFromTrades(Deck deck)
        {
            var tradesWithCapAmount = deck.TradesFrom
                .GroupJoin( deck.Cards,
                    t => t.CardId,
                    ca => ca.CardId,
                    (trade, actuals) => (trade, actuals))
                .SelectMany(
                    ta => ta.actuals.DefaultIfEmpty(),
                    (ta, actual) => (ta.trade, actual?.Amount ?? 0));

            foreach (var (trade, cap) in tradesWithCapAmount)
            {
                // modifications are not saved
                trade.Amount = Math.Min(trade.Amount, cap);
            }

            deck.TradesFrom.RemoveAll(t => t.Amount == 0);
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
                return RedirectToPage("Review", new { deckId = Deck?.Id });
            }

            var acceptRequest = GetAcceptRequest(trade);

            if (acceptRequest == null)
            {
                PostMessage = "Source Deck lacks the cards to complete the trade";
                return RedirectToPage("Review", new { deckId = Deck?.Id });
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

            return RedirectToPage("Review", new { deckId = Deck?.Id });
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

            return await TradeForReview(tradeId, tradeCard)
                .SingleOrDefaultAsync();
        }


        private IQueryable<Trade> TradeForReview(int tradeId, Card tradeCard)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Trades
                .Where(t => t.Id == tradeId && t.From.OwnerId == userId)

                .Include(t => t.To.Cards
                    .Where(ca => ca.CardId == tradeCard.Id))
                    .ThenInclude(ca => ca.Card)

                .Include(t => t.To.Wants
                    // unbounded: keep eye on
                    .Where(cr => !cr.IsReturn && cr.Card.Name == tradeCard.Name))
                    .ThenInclude(cr => cr.Card)

                .Include(t => t.From.Cards
                    .Where(ca => ca.CardId == tradeCard.Id))
                    .ThenInclude(ca => ca.Card)

                .Include(t => t.From.Wants
                    .Where(cr => !cr.IsReturn && cr.CardId == tradeCard.Id))
                    .ThenInclude(cr => cr.Card)

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

            var toTakes = trade.To.Wants
                .Where(cr => !cr.IsReturn && cr.Card.Name == trade.Card.Name);

            return new AcceptRequest(
                Trade: trade,

                ToTakes: toTakes.Any()
                    ? new RequestNameGroup(toTakes) : default,

                FromAmount: trade.From.Cards
                    .Single(ca => ca.CardId == trade.CardId));
        }


        private void ApplyAccept(AcceptRequest acceptRequest, int amount)
        {
            var acceptAmount = Math.Max(amount, 1);

            ModifyAmountsAndRequests(acceptRequest, acceptAmount);

            var (trade, toTakes, fromAmount) = acceptRequest;

            if (fromAmount.Amount == 0)
            {
                _dbContext.Amounts.Remove(fromAmount);
            }

            var finishedRequests = toTakes
                ?.Where(cr  => cr.Amount == 0)
                ?? Enumerable.Empty<CardRequest>();

            _dbContext.Requests.RemoveRange(finishedRequests);
            _dbContext.Trades.Remove(trade);
        }


        private void ModifyAmountsAndRequests(AcceptRequest acceptRequest, int acceptAmount)
        {
            var (trade, toTakes, fromAmount) = acceptRequest;

            if (toTakes == default)
            {
                return;
            }

            var (toAmount, fromTake) = GetAcceptTargets(acceptRequest);

            var exactTake = toTakes
                .SingleOrDefault(cr => cr.CardId == trade.CardId);

            int change = new [] {
                acceptAmount, trade.Amount,
                toTakes.Amount, fromAmount.Amount }.Min();

            if (exactTake != default)
            {
                int exactChange = Math.Min(change, exactTake.Amount);
                int nonExactChange = change - exactChange;

                // exactRequest mod is also reflected in toRequests
                exactTake.Amount -= exactChange;
                toTakes.Amount -= nonExactChange;
            }
            else
            {
                toTakes.Amount -= change;
            }

            toAmount.Amount += change;

            fromAmount.Amount -= change;
            fromTake.Amount += change;

            var newChange = new Change
            {
                Card = trade.Card,
                To = toAmount.Location,
                From = fromAmount.Location,
                Amount = change,
                Transaction = new()
            };

            _dbContext.Changes.Attach(newChange);
        }


        private AcceptTargets GetAcceptTargets(AcceptRequest request)
        {
            var trade = request.Trade;

            var toAmount = trade.To.Cards
                .SingleOrDefault(ca => ca.CardId == trade.CardId);

            if (toAmount is null)
            {
                toAmount = new()
                {
                    Card = trade.Card,
                    Location = trade.To,
                    Amount = 0
                };

                _dbContext.Amounts.Attach(toAmount);
            }

            var fromTake = trade.From.Wants
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
            var (trade, toTakes, _) = acceptRequest;

            if (toTakes == default)
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

            if (toTakes.Amount == 0)
            {
                _dbContext.Trades.RemoveRange(remainingTrades);
                return;
            }

            foreach (var remaining in remainingTrades)
            {
                remaining.Amount = toTakes.Amount;
            }
        }




        public async Task<IActionResult> OnPostRejectAsync(int tradeId)
        {
            if (tradeId == default)
            {
                PostMessage = "Trade is not specified";
                return RedirectToPage("Review", new { deckId = Deck?.Id });
            }

            var userId = _userManager.GetUserId(User);

            var deckTrade = await _dbContext.Trades
                .SingleOrDefaultAsync(t => 
                    t.Id == tradeId && t.From.OwnerId == userId);

            if (deckTrade == default)
            {
                PostMessage = "Trade could not be found";
                return RedirectToPage("Review", new { deckId = Deck?.Id });
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

            return RedirectToPage("Review", new { deckId = Deck?.Id });
        }
    }
}