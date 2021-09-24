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

        public IEnumerable<Exchange>? Trades { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            if (deckId == default)
            {
                return NotFound();
            }

            var deck = await DeckWithFromTrades(deckId).SingleOrDefaultAsync();

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
            Trades = deck.ExchangesFrom;

            return Page();
        }


        private IQueryable<Deck> DeckWithFromTrades(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)
                .Include(d => d.Owner)

                .Include(d => d.ExchangesFrom)
                    .ThenInclude(ex => ex.Card)

                .Include(d => d.ExchangesFrom)
                    .ThenInclude(ex => ex.To!.Owner)

                .Include(d => d.ExchangesFrom
                    .Where(ex => ex.IsTrade)
                    .OrderBy(ex => ex.Card.Name))

                .AsNoTrackingWithIdentityResolution();
        }


        private record AcceptRequest(
            Exchange Trade, 
            ExchangeNameGroup? ToRequests,
            CardAmount FromAmount) { }

        private record AcceptTargets(
            CardAmount ToAmount,
            Exchange FromRequest) { }


        public async Task<IActionResult> OnPostAcceptAsync(int tradeId)
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
                PostMessage = "Source Deck lacks the required amount to complete the trade";
                return RedirectToPage("./Review",
                    new { deckId = trade.FromId });
            }

            ApplyAccept(acceptRequest);
            await CascadeIfComplete(acceptRequest);

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


        private void ApplyAccept(AcceptRequest acceptRequest)
        {
            var (trade, toRequests, fromAmount) = acceptRequest;

            if (toRequests is not null)
            {
                var (toAmount, fromRequest) = GetAcceptTargets(acceptRequest);

                var changeOptions = new [] { trade.Amount, fromAmount.Amount, toRequests.Amount };
                var change = changeOptions.Min();

                // TODO: prioritize change to exact request amount
                toAmount.Amount += change;
                toRequests.Amount -= change;
                fromAmount.Amount -= change;
                fromRequest.Amount += change;
            }

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



        private async Task CascadeIfComplete(AcceptRequest acceptRequest)
        {
            var requests = acceptRequest.ToRequests;

            if (requests?.Amount > 0)
            {
                return;
            }

            var accepted = acceptRequest.Trade;

            // keep eye on, could possibly remove trades not started
            // by the user
            // current makes the assumption that trades are always started by
            // the owner of the To deck
            var remainingTrades = await _dbContext.Exchanges
                .Where(ex => ex.IsTrade
                    && ex.Id != accepted.Id 
                    && ex.ToId == accepted.ToId
                    && ex.Card.Name == accepted.Card.Name)
                .ToListAsync();

            _dbContext.Exchanges.RemoveRange(remainingTrades);
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