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
    public class SuggestModel : PageModel
    {
        public record DeckColor(Deck Deck, IEnumerable<string> Colors) { }


        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        public SuggestModel(CardDbContext dbContext, UserManager<CardUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }



        [TempData]
        public string PostMessage { get; set; }

        public Card Card { get; private set; }

        public IReadOnlyList<CardUser> Users { get; private set; }

        public IReadOnlyList<DeckColor> DeckColors { get; private set; }



        public async Task<IActionResult> OnGetAsync(string cardId)
        {
            var card = await _dbContext.Cards.FindAsync(cardId);

            if (card is null)
            {
                return NotFound();
            }

            var srcId = _userManager.GetUserId(User);

            Card = card;

            Users = await _userManager.Users
                .Where(u => u.Id != srcId)
                .AsNoTracking()
                .ToListAsync();

            return Page();
        }


        public async Task<IActionResult> OnPostUserAsync(string cardId, string userId)
        {
            var card = await _dbContext.Cards.FindAsync(cardId);

            if (card is null)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(userId);

            if (user is null)
            {
                return NotFound();
            }

            var decks = await GetDeckOptionsAsync(card, user);

            var colors = decks
                .Select(d => d
                    .GetColors()
                    .Select(c => Color.COLORS[ c.Name.ToLower() ]));

            Card = card;

            DeckColors = decks
                .Zip(colors, (deck, color) => new DeckColor(deck, color))
                .ToList();

            return Page();
        }


        private async Task<IEnumerable<Deck>> GetDeckOptionsAsync(Card card, CardUser user)
        {
            var userDecks = await _dbContext.Decks
                .Where(l => l.OwnerId == user.Id)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)
                        .ThenInclude(c => c.Colors)
                .AsSplitQuery()
                .ToListAsync();

            if (!userDecks.Any())
            {
                return Enumerable.Empty<Deck>();
            }

            // include both request and non-request amounts
            var decksWithCard = await _dbContext.Amounts
                .Where(ca => ca.CardId == card.Id && ca.Location is Deck)
                .Select(ca => ca.Location as Deck)
                .Where(d => d.OwnerId == user.Id)
                .Distinct()
                .ToListAsync();

            var transfersWithCard = await _dbContext.Transfers
                .Where(t => t.CardId == card.Id
                    && (t.ProposerId == user.Id || t.ReceiverId == user.Id))
                .Select(t => t.To)
                .Distinct()
                .ToListAsync();

            var invalidDecks = decksWithCard
                .Concat(transfersWithCard)
                .Distinct();

            return userDecks.Except(invalidDecks);
        }


        public async Task<IActionResult> OnPostDeckAsync(string cardId, int deckId)
        {
            var card = await _dbContext.Cards.FindAsync(cardId);

            if (card is null)
            {
                return NotFound();
            }

            if (deckId == default)
            {
                DeckColors = new List<DeckColor>();
                return Page();
            }

            var toDeck = await GetSuggestionDeckAsync(card, deckId);

            if (toDeck is null)
            {
                return RedirectToPage("./Index");
            }

            await _dbContext.Entry(toDeck)
                .Reference(d => d.Owner)
                .LoadAsync();

            var fromUser = await _userManager.GetUserAsync(User);

            var suggestion = new Suggestion
            {
                Card = card,
                Proposer = fromUser,
                Receiver = toDeck.Owner,
                To = toDeck
            };

            _dbContext.Attach(suggestion);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Suggestion Successfully Created";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into issue while creating Suggestion";
            }

            return RedirectToPage("./Index");
        }


        private async Task<Deck> GetSuggestionDeckAsync(Card card, int deckId)
        {
            var deck = await _dbContext.Decks
                .Include(d => d.Cards)
                .Include(d => d.Owner)
                .SingleOrDefaultAsync(d => d.Id == deckId);

            if (deck is null)
            {
                PostMessage = "Suggestion target is not valid";
                return null;
            }

            // include both suggestions and trades
            var suggestPrior = await _dbContext.Suggestions
                .AnyAsync(t => 
                    t.ReceiverId == deck.OwnerId && t.CardId == card.Id);

            if (suggestPrior)
            {
                PostMessage = "Suggestion is redundant";
                return null;
            }

            var suggestInDeck = deck.Cards
                .Select(c => c.CardId)
                .Contains(card.Id);

            if (suggestInDeck)
            {
                PostMessage = "Suggestion is already in deck";
                return null;
            }            

            return deck;
        }
    }

}