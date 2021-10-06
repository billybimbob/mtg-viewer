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

        public IReadOnlyList<UserRef> Users { get; private set; }


        public async Task<IActionResult> OnGetAsync(string cardId)
        {
            var card = await _dbContext.Cards.FindAsync(cardId);

            if (card is null)
            {
                return NotFound();
            }

            Card = card;
            Users = await NotSuggestedUsers(card).ToListAsync();

            return Page();
        }


        public IQueryable<UserRef> NotSuggestedUsers(Card card)
        {
            var proposerId = _userManager.GetUserId(User);

            var nonProposers = _dbContext.Users
                .Where(u => u.Id != proposerId);

            var cardSuggests = _dbContext.Suggestions
                .Where(s => s.Card.Name == card.Name && s.ToId == default);

            return nonProposers
                .GroupJoin( cardSuggests,
                    user => user.Id,
                    suggest => suggest.ReceiverId,
                    (user, suggests) =>
                        new { user, suggests })
                .SelectMany(
                    uss => uss.suggests.DefaultIfEmpty(),
                    (uss, suggest) =>
                        new { uss.user, suggest })

                .Where(us => us.suggest == default)
                .Select(us => us.user)
                .AsNoTracking();
        }



        public UserRef Receiver { get; private set; }
        public IReadOnlyList<Deck> Decks { get; private set; }


        public async Task<IActionResult> OnPostUserAsync(string cardId, string userId)
        {
            var card = await _dbContext.Cards.FindAsync(cardId);

            if (card is null)
            {
                return NotFound();
            }

            var currentUserId = _userManager.GetUserId(User);
            
            if (currentUserId == userId)
            {
                return NotFound();
            }

            var receiver = await _dbContext.Users.FindAsync(userId);

            if (receiver is null)
            {
                return NotFound();
            }

            var decks = await DecksWithoutCard(card, receiver).ToListAsync();

            Receiver = receiver;

            Card = card;
            Decks = decks;

            return Page();
        }


        private IQueryable<Deck> DecksWithoutCard(Card card, UserRef user)
        {
            var userDecks = _dbContext.Decks
                .Where(l => l.OwnerId == user.Id);

            var userCardAmounts = _dbContext.Amounts
                .Where(ca => ca.Card.Name == card.Name
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId == user.Id);


            var withoutAmounts = userDecks 
                .GroupJoin( userCardAmounts,
                    deck => deck.Id,
                    amount => amount.LocationId,
                    (deck, amounts) => new { deck, amounts })
                .SelectMany(
                    das => das.amounts.DefaultIfEmpty(),
                    (das, amount) => new { das.deck, amount })

                .Where(da => da.amount == default)
                .Select(da => da.deck);


            var suggestsWithCard = _dbContext.Suggestions
                .Where(s => s.Card.Name == card.Name 
                    && s.ReceiverId == user.Id);

            var withoutSuggests = withoutAmounts
                .GroupJoin( suggestsWithCard,
                    deck => deck.Id,
                    suggest => suggest.ToId,
                    (deck, suggests) => new { deck, suggests })
                .SelectMany(
                    dts => dts.suggests.DefaultIfEmpty(),
                    (dts, suggest) => new { dts.deck, suggest })

                .Where(dt => dt.suggest == default)
                .Select(dt => dt.deck);


            var tradesWithCard = _dbContext.Trades
                .Where(t => t.Card.Name == card.Name && t.To.OwnerId == user.Id);

            var withoutTrades = withoutSuggests
                .GroupJoin( tradesWithCard,
                    deck => deck.Id,
                    transfer => transfer.ToId,
                    (deck, trades) => new { deck, trades })
                .SelectMany(
                    dts => dts.trades.DefaultIfEmpty(),
                    (dts, trade) => new { dts.deck, trade })

                .Where(dt => dt.trade == default)
                .Select(dt => dt.deck);


            return withoutTrades
                .Include(d => d.Cards)
                    // unbounded: keep eye on
                    .ThenInclude(ca => ca.Card)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }



        [BindProperty]
        public Suggestion Suggestion { get; set; }


        public async Task<IActionResult> OnPostSuggestAsync()
        {
            _dbContext.Suggestions.Attach(Suggestion);

            if (!await IsValidSuggestionAsync(Suggestion))
            {
                return RedirectToPage("./Index");
            }

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


        private async Task<bool> IsValidSuggestionAsync(Suggestion suggestion)
        {
            // include both suggestions and trades
            var suggestPrior = await _dbContext.Suggestions
                .AnyAsync(t =>
                    t.ReceiverId == suggestion.ReceiverId
                        && t.CardId == suggestion.CardId
                        && t.ToId == suggestion.ToId);

            if (suggestPrior)
            {
                PostMessage = "Suggestion is redundant";
                return false;
            }

            if (suggestion.ToId is null)
            {
                return true;
            }

            await _dbContext.Entry(suggestion)
                .Reference(s => s.To)
                .LoadAsync();

            if (suggestion.ReceiverId != suggestion.To?.OwnerId)
            {
                PostMessage = "Suggestion target is not valid";
                return false;
            }

            await _dbContext.Entry(suggestion.To)
                .Collection(t => t.Cards)
                .LoadAsync();

            var suggestInDeck = suggestion.To.Cards
                .Select(c => c.CardId)
                .Contains(suggestion.CardId);

            if (suggestInDeck)
            {
                PostMessage = "Suggestion is already in deck";
                return false;
            }

            return true;
        }
    }

}