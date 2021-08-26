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
        public enum DeckState
        {
            Invalid,
            Valid,
            Requesting
        }

        public record DeckColor(Deck Deck, IEnumerable<string> Colors, DeckState State) { }


        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public IndexModel(UserManager<CardUser> userManager, CardDbContext context)
        {
            _userManager = userManager;
            _dbContext = context;
        }


        [TempData]
        public string PostMessage { get; set; }

        public CardUser CardUser { get; private set; }
        public IReadOnlyList<DeckColor> DeckColors { get; private set; }


        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            var decks = await _dbContext.Decks
                .Where(d => d.OwnerId == user.Id)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)
                .ToListAsync();

            var colors = decks.Select(d => d.GetColorSymbols());
            var states = GetDeckStates(
                decks, await GetRequestingAsync(user));

            CardUser = user;
            DeckColors = decks
                .Zip(colors, (deck, color) => (deck, color))
                .Zip(states, (dc, state) => new DeckColor(dc.deck, dc.color, state))
                .ToList();
        }


        private async Task<IReadOnlyList<Deck>> GetRequestingAsync(CardUser user)
        {
            var userDecks = _dbContext.Decks
                .Where(d => d.OwnerId == user.Id);

            var userTrades = _dbContext.Trades
                .Where(t => t.ProposerId == user.Id);

            return await userDecks
                .Join( userTrades,
                    d => d.Id, t => t.ToId, (deck, trade) => deck)
                .Distinct()
                .ToListAsync();
        }


        private IEnumerable<DeckState> GetDeckStates(IEnumerable<Deck> decks, IEnumerable<Deck> requestDecks)
        {
            requestDecks = requestDecks.ToHashSet();

            foreach(var deck in decks)
            {
                if (requestDecks.Contains(deck))
                {
                    yield return DeckState.Requesting;
                }
                else if (deck.Cards.Any(ca => ca.IsRequest))
                {
                    yield return DeckState.Invalid;
                }
                else
                {
                    yield return DeckState.Valid;
                }
            }
        }
    }
}