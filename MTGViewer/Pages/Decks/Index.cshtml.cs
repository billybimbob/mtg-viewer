using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    public enum State
    {
        Invalid,
        Valid,
        Requesting
    }

    public record DeckState(Deck Deck, State State)
    {
        public DeckState(Deck deck, bool isRequest)
            : this(deck, State.Invalid)
        {
            if (isRequest)
            {
                State = State.Requesting;
            }
            else if (deck.Cards.Any(da => da.Intent != Intent.None))
            {
                State = State.Invalid;
            }
            else
            {
                State = State.Valid;
            }
        }
    }


    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public IndexModel(UserManager<CardUser> userManager, CardDbContext context)
        {
            _userManager = userManager;
            _dbContext = context;
        }


        [TempData]
        public string PostMessage { get; set; }

        public UserRef CardUser { get; private set; }
        public IReadOnlyList<DeckState> Decks { get; private set; }


        public async Task OnGetAsync()
        {
            var userId = _userManager.GetUserId(User);

            Decks = await GetDeckStatesAsync(userId);

            CardUser = Decks.FirstOrDefault()?.Deck.Owner
                ?? await _dbContext.Users.FindAsync(userId);
        }


        private async Task<IReadOnlyList<DeckState>> GetDeckStatesAsync(string userId)
        {
            var userDecks = _dbContext.Decks
                .Where(d => d.OwnerId == userId)
                .Include(d => d.Owner)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card);

            var userTrades = _dbContext.Trades
                .Where(t => t.ProposerId == userId);


            var deckRequestInfos = await userDecks
                .GroupJoin( userTrades,
                    deck => deck.Id,
                    trade => trade.ToId,
                    (deck, trades) =>
                        new { deck, trades })
                .SelectMany(
                    dts => dts.trades.DefaultIfEmpty(),
                    (dts, trade) => 
                        new { dts.deck, isRequest = trade != default})
                .ToListAsync();


            return deckRequestInfos
                .Distinct()
                .OrderBy(db => db.deck.Name)
                .Select(db => new DeckState(db.deck, db.isRequest))
                .ToList();
        }
    }
}