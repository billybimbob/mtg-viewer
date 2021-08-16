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


namespace MTGViewer.Pages.Trades
{
    [Authorize]
    public class SuggestModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;

        public SuggestModel(CardDbContext dbContext, UserManager<CardUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }


        private string CardId
        {
            // gets casted to guid for some reason
            get => TempData[nameof(CardId)].ToString();
            set => TempData[nameof(CardId)] = value;
        }

        [TempData]
        public string PostMessage { get; set; }

        public IEnumerable<CardUser> Users { get; private set; }

        public IEnumerable<(Deck, IEnumerable<string>)> Decks { get; private set; }

        public Card Suggesting { get; private set; }

        private async Task SetSuggestingAsync() =>
            Suggesting = await _dbContext.Cards.FindAsync(CardId);


        public async Task<IActionResult> OnGetAsync(string cardId)
        {
            CardId = cardId;

            await SetSuggestingAsync();

            if (Suggesting is null)
            {
                return NotFound();
            }

            var srcId = _userManager.GetUserId(User);

            Users = await _userManager.Users
                .Where(u => u.Id != srcId)
                .AsNoTracking()
                .ToListAsync();

            TempData.Keep(nameof(CardId));

            return Page();
        }


        public async Task<IActionResult> OnPostUserAsync(string userId)
        {
            await SetSuggestingAsync();

            if (Suggesting is null)
            {
                return NotFound();
            }

            var decks = await GetDeckOptionsAsync(userId);

            var deckColors = decks
                .Select(d => d
                    .GetColors()
                    .Select(c => Color.COLORS[ c.Name.ToLower() ]));

            Decks = decks.Zip(deckColors);

            TempData.Keep(nameof(CardId));

            return Page();
        }


        private async Task<IEnumerable<Deck>> GetDeckOptionsAsync(string userId)
        {
            var userDecks = await _dbContext.Decks
                .Where(l => l.OwnerId == userId)
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
            var suggestInDeck = await _dbContext.Amounts
                .Where(ca =>
                    !ca.Location.IsShared && ca.CardId == Suggesting.Id)
                .Select(ca => ca.Location as Deck)
                .Where(d => d.OwnerId == userId)
                .ToListAsync();

            // include both suggestions and trades
            var suggestPrior = await _dbContext.Transfers
                .Where(t =>
                    t.ReceiverId == userId && t.CardId == Suggesting.Id)
                .Select(t => t.To)
                .Distinct()
                .ToListAsync();

            var invalidDecks = suggestInDeck
                .Concat(suggestPrior)
                .Distinct();

            return userDecks.Except(invalidDecks);
        }


        public async Task<IActionResult> OnPostDeckAsync(int deckId)
        {
            await SetSuggestingAsync();

            if (Suggesting is null)
            {
                return NotFound();
            }

            var toDeck = await GetDeckAndValidateAsync(deckId);

            if (toDeck is null)
            {
                return RedirectToPage("./Index");
            }

            await _dbContext.Entry(toDeck)
                .Reference(d => d.Owner)
                .LoadAsync();

            var fromUser = await _userManager.GetUserAsync(User);

            var suggestion = new Transfer
            {
                Card = Suggesting,
                Proposer = fromUser,
                Receiver = toDeck.Owner,
                To = toDeck
            };

            _dbContext.Attach(suggestion);

            await _dbContext.SaveChangesAsync();

            PostMessage = "Suggestion Successfully Created";

            return RedirectToPage("./Index");
        }


        private async Task<Deck> GetDeckAndValidateAsync(int deckId)
        {
            var deck = await _dbContext.Decks.FindAsync(deckId);

            if (deck is null || deck.IsShared)
            {
                PostMessage = "Suggestion target is not valid";
                return null;
            }

            // include both suggestions and trades
            var suggestPrior = await _dbContext.Transfers
                .Where(t => 
                    t.ReceiverId == deck.OwnerId && t.CardId == Suggesting.Id)
                .AnyAsync();

            if (suggestPrior)
            {
                PostMessage = "Suggestion is redundant";
                return null;
            }

            await _dbContext.Entry(deck)
                .Collection(d => d.Cards)
                .LoadAsync();

            var suggestInDeck = deck.Cards
                .Select(c => c.CardId)
                .Contains(Suggesting.Id);

            if (suggestInDeck)
            {
                PostMessage = "Suggestion is already in deck";
                return null;
            }            

            return deck;
        }
    }

}