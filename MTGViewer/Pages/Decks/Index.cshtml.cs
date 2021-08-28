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
    [Authorize]
    public class IndexModel : PageModel
    {
        public enum State
        {
            Invalid,
            Valid,
            Requesting
        }

        public record DeckState(Deck Deck, State State) { }


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
            var user = await _dbContext.Users.FindAsync(userId);

            var decks = await _dbContext.Decks
                .Where(d => d.OwnerId == user.Id)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)
                .ToListAsync();

            var states = GetDeckStates(
                decks, await GetRequestingAsync(user));

            CardUser = user;
            Decks = decks
                .Zip(states, (deck, state) => new DeckState(deck, state))
                .ToList();
        }


        private async Task<IReadOnlyList<Deck>> GetRequestingAsync(UserRef user)
        {
            var userDecks = _dbContext.Decks
                .Where(d => d.OwnerId == user.Id);

            var userTrades = _dbContext.Trades
                .Where(t => t.ProposerId == user.Id);

            return await userDecks
                .Join( userTrades,
                    deck => deck.Id,
                    trade => trade.ToId,
                    (deck, trade) => deck)
                .Distinct()
                .ToListAsync();
        }


        private IEnumerable<State> GetDeckStates(IEnumerable<Deck> decks, IEnumerable<Deck> requestDecks)
        {
            requestDecks = requestDecks.ToHashSet();

            foreach(var deck in decks)
            {
                if (requestDecks.Contains(deck))
                {
                    yield return State.Requesting;
                }
                else if (deck.Cards.Any(ca => ca.IsRequest))
                {
                    yield return State.Invalid;
                }
                else
                {
                    yield return State.Valid;
                }
            }
        }
    }
}