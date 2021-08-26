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

        public CardUser PickedUser { get; private set; }
        public IReadOnlyList<DeckColor> DeckColors { get; private set; }

        [BindProperty]
        public Suggestion Suggestion { get; set; }


        public async Task<IActionResult> OnGetAsync(string cardId)
        {
            var card = await _dbContext.Cards.FindAsync(cardId);

            if (card is null)
            {
                return NotFound();
            }

            Card = card;
            Users = await GetPossibleUsersAsync(cardId);

            return Page();
        }


        public async Task<IReadOnlyList<CardUser>> GetPossibleUsersAsync(string cardId)
        {
            var proposerId = _userManager.GetUserId(User);

            var nonProposers = _userManager.Users
                .Where(u => u.Id != proposerId);

            var cardSuggests = _dbContext.Suggestions
                .Where(s => s.CardId == cardId && s.ToId == default);

            var notSuggested = nonProposers
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
                .Select(us => us.user);

            return await notSuggested
                .AsNoTracking()
                .ToListAsync();
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

            var decks = await GetValidDecksAsync(card, user);
            var colors = decks.Select(d => d.GetColorSymbols());

            Card = card;
            PickedUser = user;
            DeckColors = decks
                .Zip(colors, (deck, color) => new DeckColor(deck, color))
                .ToList();

            return Page();
        }



        private async Task<IReadOnlyList<Deck>> GetValidDecksAsync(Card card, CardUser user)
        {
            var userDecks = _dbContext.Decks
                .Where(l => l.OwnerId == user.Id)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card);

            var userCardAmounts = _dbContext.Amounts
                .Where(ca => ca.CardId == card.Id
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId == user.Id);

            var decksWithoutCard = userDecks
                .GroupJoin( userCardAmounts,
                    d => d.Id,
                    ca => ca.LocationId,
                    (deck, amounts) => new { deck, amounts })
                .SelectMany(
                    das => das.amounts.DefaultIfEmpty(),
                    (das, amount) => new { das.deck, amount })
                .Where(da => da.amount == default)
                .Select(da => da.deck);


            var transfersWithCard = _dbContext.Transfers
                .Where(t => t.CardId == card.Id
                    && (t.ProposerId == user.Id || t.ReceiverId == user.Id));

            var validDecks = decksWithoutCard
                .GroupJoin( transfersWithCard,
                    d => d.Id,
                    t => t.ToId,
                    (deck, transfers) => new { deck, transfers })
                .SelectMany(
                    dts => dts.transfers.DefaultIfEmpty(),
                    (dts, transfer) => new { dts.deck, transfer })
                .Where(dt => dt.transfer == default)
                .Select(dt => dt.deck);


            return await validDecks
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync();
        }


        public async Task<IActionResult> OnPostSuggestAsync()
        {
            _dbContext.Attach(Suggestion);

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
            suggestion.ProposerId = _userManager.GetUserId(User);

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