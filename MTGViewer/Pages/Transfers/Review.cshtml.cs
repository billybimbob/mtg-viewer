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


namespace MTGViewer.Pages.Transfers
{
    [Authorize]
    public class ReviewModel : PageModel
    {
        private record AcceptAmounts(
            IReadOnlyList<Trade> Accepts,
            IDictionary<int, CardAmount> ToAmounts,
            IReadOnlyDictionary<int, CardAmount> FromAmounts) { }


        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        public ReviewModel(CardDbContext dbContext, UserManager<CardUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }


        [TempData]
        public string PostMessage { get; set; }

        public CardUser Proposer { get; private set; }
        public Deck Deck { get; private set; }

        public IReadOnlyList<Trade> ToDeck { get; private set; }
        public IReadOnlyList<Trade> FromDeck { get; private set; }


        public async Task<IActionResult> OnGetAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Decks
                .Include(d => d.Owner)
                .SingleOrDefaultAsync(d =>
                    d.Id == deckId && d.OwnerId != proposerId);

            if (deck == null)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(proposerId, deck.Id))
                .Include(t => t.Card)
                .Include(t => t.To)
                .Include(t => t.From)
                .AsNoTracking()
                .ToListAsync();

            if (!CheckTrades(deckTrades))
            {
                PostMessage = "Not all specified trades are valid";
                return RedirectToPage("./Index");
            }

            Deck = deck;

            Proposer = await _userManager.FindByIdAsync(proposerId);

            ToDeck = deckTrades
                .Where(t => t.To.Id == deck.Id)
                .OrderBy(t => t.Card.Name)
                .ToList();

            FromDeck = deckTrades
                .Except(ToDeck)
                .OrderBy(t => t.Card.Name)
                .ToList();

            return Page();
        }


        private bool CheckTrades(IEnumerable<Trade> trades)
        {
            if (!trades.Any())
            {
                return false;
            }

            var userId = _userManager.GetUserId(User);

            return trades.All(t => t.IsInvolved(userId))
                && trades.All(t => t.IsWaitingOn(userId));
        }



        public async Task<IActionResult> OnPostAcceptAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var validDeck = await _dbContext.Decks
                .AnyAsync(d => d.Id == deckId && d.OwnerId != proposerId);

            if (!validDeck)
            {
                return NotFound();
            }

            var acceptInfo = await GetAcceptInfoAsync(proposerId, deckId);

            if (acceptInfo == null)
            {
                return RedirectToPage("./Index");
            }

            ApplyAccepts(acceptInfo);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Trade successfully Applied";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while Accepting";
            }

            return RedirectToPage("./Index");
        }


        private async Task<AcceptAmounts> GetAcceptInfoAsync(string proposerId, int deckId)
        {
            var acceptQuery = _dbContext.Trades
                .Where(TradeFilter.Involves(proposerId, deckId));

            var deckTrades = await acceptQuery.ToListAsync();

            if (!CheckTrades(deckTrades))
            {
                PostMessage = "Not all specified trades are valid";
                return null;
            }

            var sourceMap = await acceptQuery
                .Join( _dbContext.Amounts.Where(ca => !ca.IsRequest),
                    trade =>
                        new { trade.CardId, DeckId = trade.FromId },
                    amount =>
                        new { amount.CardId, DeckId = amount.LocationId },
                    (trade, amount) =>
                        new { trade.Id, amount })
                .ToDictionaryAsync(r => r.Id, r => r.amount);


            var amountsInvalid = deckTrades.Any(t => 
                !sourceMap.TryGetValue(t.Id, out var source) || source.Amount < t.Amount);

            if (amountsInvalid)
            {
                PostMessage = "Source Deck lacks the required amount to complete the trade";
                return null;
            }

            var destMap = await acceptQuery
                .Join( _dbContext.Amounts.Where(ca => !ca.IsRequest),
                    trade => 
                        new { trade.CardId, DeckId = trade.ToId },
                    amount =>
                        new { amount.CardId, DeckId = amount.LocationId },
                    (trade, amount) =>
                        new { trade.Id, amount })
                .ToDictionaryAsync(t => t.Id, t => t.amount);


            return new AcceptAmounts(
                Accepts: deckTrades,
                ToAmounts: destMap,
                FromAmounts: sourceMap);
        }


        private void ApplyAccepts(AcceptAmounts acceptInfo)
        {
            foreach(var accept in acceptInfo.Accepts)
            {
                var sourceAmount = acceptInfo.FromAmounts[accept.Id];

                if (!acceptInfo.ToAmounts.TryGetValue(accept.Id, out var destAmount))
                {
                    destAmount = new CardAmount
                    {
                        Card = accept.Card,
                        Location = accept.To,
                        Amount = 0
                    };

                    acceptInfo.ToAmounts.Add(accept.Id, destAmount);
                    _dbContext.Amounts.Add(destAmount);
                }

                sourceAmount.Amount -= accept.Amount;
                destAmount.Amount += accept.Amount;
            }

            _dbContext.Trades.RemoveRange(acceptInfo.Accepts);
        }



        public async Task<IActionResult> OnPostRejectAsync(string proposerId, int deckId)
        {
            if (proposerId == null)
            {
                return NotFound();
            }

            var deck = await _dbContext.Decks.FindAsync(deckId);

            if (deck == null || deck.OwnerId == proposerId)
            {
                return NotFound();
            }

            var deckTrades = await _dbContext.Trades
                .Where(TradeFilter.Involves(proposerId, deckId))
                .ToListAsync();

            if (!CheckTrades(deckTrades))
            {
                PostMessage = "Not all specified trades are valid";
                return RedirectToPage("./Index");
            }

            _dbContext.Trades.RemoveRange(deckTrades);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully rejected Trade";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while rejecting";
            }

            return RedirectToPage("./Index");
        }
    }
}