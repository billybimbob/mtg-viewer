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

            var userId = _userManager.GetUserId(User);
            
            var deck = await _dbContext.Decks
                .Include(d => d.Owner)
                .Include(d => d.ExchangesFrom)
                    .ThenInclude(ex => ex.Card)
                .Include(d => d.ExchangesFrom
                    .Where(ex => ex.IsTrade)
                    .OrderBy(ex => ex.Card.Name))
                    .ThenInclude(ex => ex.To!.Owner)
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(d =>
                    d.Id == deckId && d.OwnerId == userId);

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



        private class AcceptAmounts
        {
            public Exchange Accept { get; }
            public CardAmount? ToAmount { get; }
            public CardAmount FromAmount { get; }
            public ExchangeNameGroup? ToTakes { get; }
            public Exchange? FromTakes { get; }

            public AcceptAmounts(
                Exchange trade,
                IEnumerable<CardAmount> amounts,
                IEnumerable<Exchange> takes)
            {
                Accept = trade;

                ToAmount = amounts.SingleOrDefault(ca => ca.LocationId == trade.ToId);

                var toTakes = takes.Where(ex => ex.ToId == trade.ToId);

                ToTakes = toTakes.Any()
                    ? new ExchangeNameGroup(toTakes)
                    : default;

                FromAmount = amounts.Single(ca => ca.LocationId == trade.FromId);

                FromTakes = takes.SingleOrDefault(ex => ex.FromId == trade.FromId);
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


        private async Task<Exchange?> GetDeckTradeAsync(int tradeId)
        {
            if (tradeId == default)
            {
                return null;
            }

            var userId = _userManager.GetUserId(User);

            var deckTrade = await _dbContext.Exchanges
                .Include(ex => ex.Card)
                .SingleOrDefaultAsync(ex => ex.IsTrade
                    && ex.Id == tradeId 
                    && ex.From != default
                    && ex.From!.OwnerId == userId);

            if (deckTrade == default)
            {
                return null;
            }

            return deckTrade;
        }


        private async Task<AcceptAmounts?> GetAcceptAmountsAsync(Exchange trade)
        {
            var tradeDecks = await TradeTargets(trade).ToListAsync();

            var amountsValid = tradeDecks
                .Any(d => d.Id == trade.FromId && d.Cards.Any());

            if (!amountsValid)
            {
                return null;
            }

            var tradeAmounts = tradeDecks.SelectMany(d => d.Cards).ToList();
            var tradeRequests = tradeDecks.SelectMany(d => d.ExchangesTo).ToList();

            return new AcceptAmounts(trade, tradeAmounts, tradeRequests);
        }


        private IQueryable<Deck> TradeTargets(Exchange trade)
        {
            var tradeDecks = _dbContext.Decks
                .Where(d => d.Id == trade.FromId || d.Id == trade.ToId);

            var withCards = tradeDecks
                .Include(d => d.Cards
                    .Where(ca => ca.CardId == trade.CardId));

            var withTakes = withCards
                .Include(d => d.ExchangesTo
                    .Where(ex => !ex.IsTrade)
                    .Where(ex =>
                        ex.ToId == trade.FromId
                            && ex.CardId == trade.CardId
                        || ex.ToId == trade.ToId
                            && ex.Card.Name == trade.Card.Name));

            return withTakes.AsSplitQuery();
        }


        private void ApplyAccept(AcceptAmounts acceptInfo)
        {
            var accept = acceptInfo.Accept;
            var toTakes = acceptInfo.ToTakes;
            var fromAmount = acceptInfo.FromAmount;

            if (toTakes is not null)
            {
                var toAmount = acceptInfo.ToAmount;
                var fromTakes = acceptInfo.FromTakes;

                if (toAmount is null)
                {
                    toAmount = new()
                    {
                        Card = accept.Card,
                        Location = accept.To!,
                        Amount = 0
                    };

                    _dbContext.Amounts.Attach(toAmount);
                }

                if (fromTakes is null)
                {
                    fromTakes = new()
                    {
                        Card = accept.Card,
                        From = accept.From,
                        Amount = 0
                    };

                    _dbContext.Exchanges.Attach(fromTakes);
                }

                var changeOptions = new [] { accept.Amount, fromAmount.Amount, toTakes.Amount };
                var change = changeOptions.Min();

                // TODO: prioritize change to exact request amount
                toAmount.Amount += change;
                toTakes.Amount -= change;
                fromAmount.Amount -= change;
                fromTakes.Amount += change;
            }

            var finishedRequests = toTakes
                ?.Where(ca  => ca.Amount == 0)
                ?? Enumerable.Empty<Exchange>();

            if (finishedRequests.Any())
            {
                _dbContext.Exchanges.RemoveRange(finishedRequests);
            }

            if (fromAmount.Amount == 0)
            {
                _dbContext.Amounts.Remove(fromAmount);
            }

            _dbContext.Exchanges.Remove(accept);
        }


        private async Task CascadeIfComplete(AcceptAmounts acceptInfo)
        {
            var requests = acceptInfo.ToTakes;

            if (requests?.Amount > 0)
            {
                return;
            }

            var accepted = acceptInfo.Accept;

            // keep eye on, could possibly remove trades not started
            // by the user
            // makes the assumption that trades are always started by
            // the owner of the To deck
            var remainingTrades = await _dbContext.Exchanges
                .Where(t => t.IsTrade
                    && t.Id != accepted.Id 
                    && t.ToId == accepted.ToId
                    && t.Card.Name == accepted.Card.Name)
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