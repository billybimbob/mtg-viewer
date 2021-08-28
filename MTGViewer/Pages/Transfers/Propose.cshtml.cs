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
    public class ProposeModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public ProposeModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        [TempData]
        public string PostMessage { get; set; }

        public UserRef Proposer { get; private set; }
        public int DeckId { get; private set; }

        public IReadOnlyList<Deck> ProposeOptions { get; private set; }



        public async Task<IActionResult> OnGetAsync(string proposerId, int? deckId)
        {
            if (deckId is int id && proposerId != null)
            {
                var validDeck = await _dbContext.Decks
                    .AnyAsync(d => d.Id == id && d.OwnerId != proposerId);

                if (validDeck)
                {
                    Proposer = await _dbContext.Users.FindAsync(proposerId);
                    DeckId = id;

                    if (Proposer == null)
                    {
                        return NotFound();
                    }

                    return Page();
                }
            }

            Proposer = await _dbContext.Users.FindAsync( _userManager.GetUserId(User) );
            ProposeOptions = await GetProposeOptionsAsync(Proposer.Id);

            if (!ProposeOptions.Any())
            {
                PostMessage = "There are no decks to Trade with";
                return RedirectToPage("./Index");
            }

            return Page();
        }


        private async Task<IReadOnlyList<Deck>> GetProposeOptionsAsync(string proposerId)
        {
            var nonUserDecks = _dbContext.Decks
                .Where(d => d.OwnerId != proposerId)
                .Include(d => d.Owner);

            var userTrades = _dbContext.Trades
                .Where(TradeFilter.Involves(proposerId));

            var exceptTo = nonUserDecks
                .GroupJoin( userTrades,
                    deck => deck.Id,
                    trade => trade.ToId,
                    (deck, trades) => new { deck, trades })
                .SelectMany(
                    dts => dts.trades.DefaultIfEmpty(),
                    (dts, trade) => new { dts.deck, trade })
                .Where(dt => dt.trade == default)
                .Select(dt => dt.deck);

            var exceptTrades = exceptTo
                .GroupJoin( userTrades,
                    deck => deck.Id,
                    trade => trade.FromId,
                    (deck, trades) => new { deck, trades })
                .SelectMany(
                    dts => dts.trades.DefaultIfEmpty(),
                    (dts, trade) => new { dts.deck, trade })
                .Where(dt => dt.trade == default)
                .Select(dt => dt.deck);

            return await exceptTrades
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();
        }
    }
}