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
using MTGViewer.Services;

namespace MTGViewer.Pages.Transfers;


[Authorize]
public class SuggestModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;

    public SuggestModel(
        PageSizes pageSizes,
        CardDbContext dbContext, 
        UserManager<CardUser> userManager)
    {
        _pageSize = pageSizes.GetSize<SuggestModel>();
        _dbContext = dbContext;
        _userManager = userManager;
    }


    [TempData]
    public string PostMessage { get; set; }

    [BindProperty]
    public Card Card { get; set; }

    public PagedList<UserRef> Users { get; private set; }


    public async Task<IActionResult> OnGetAsync(string cardId, int? pageIndex)
    {
        Card = await _dbContext.Cards.FindAsync(cardId);

        if (Card is null)
        {
            return NotFound();
        }

        Users = await UsersForSuggest()
            .ToPagedListAsync(_pageSize, pageIndex);

        return Page();
    }


    public IQueryable<UserRef> UsersForSuggest()
    {
        var proposerId = _userManager.GetUserId(User);

        var nonProposers = _dbContext.Users
            .Where(u => u.Id != proposerId);

        var cardSuggests = _dbContext.Suggestions
            .Where(s => s.Card.Name == Card.Name && s.ToId == default);

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
            .OrderBy(u => u.Name)
            .AsNoTracking();
    }



    public UserRef Receiver { get; private set; }

    public IReadOnlyList<Deck> Decks { get; private set; }


    public async Task<IActionResult> OnPostUserAsync(string userId)
    {
        if (userId == null)
        {
            return RedirectToPage("Suggest", new { cardId = Card.Id });
        }

        Card = await _dbContext.Cards.FindAsync(Card.Id);

        if (Card is null)
        {
            return NotFound();
        }

        var currentUserId = _userManager.GetUserId(User);
        
        if (currentUserId == userId)
        {
            return NotFound();
        }

        Receiver = await _dbContext.Users.FindAsync(userId);

        if (Receiver is null)
        {
            return NotFound();
        }

        Decks = await DecksForSuggest().ToListAsync();

        return Page();
    }


    private IQueryable<Deck> DecksForSuggest()
    {
        var userDecks = _dbContext.Decks
            .Where(l => l.OwnerId == Receiver.Id);

        var withoutAmounts = DecksWithoutAmounts(userDecks);

        var withoutWants = DecksWithoutWants(withoutAmounts);

        var withoutSuggests = DecksWithoutSuggests(withoutWants);

        var withoutTrades = DecksWithoutTrades(withoutSuggests);

        return withoutTrades
            .OrderBy(d => d.Name)
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    private IQueryable<Deck> DecksWithoutAmounts(IQueryable<Deck> decks)
    {
        var userCardAmounts = _dbContext.Amounts
            .Where(ca => ca.Card.Name == Card.Name
                && ca.Location is Deck
                && (ca.Location as Deck).OwnerId == Receiver.Id);

        return decks 
            .GroupJoin( userCardAmounts,
                deck => deck.Id,
                amount => amount.LocationId,
                (deck, amounts) => new { deck, amounts })
            .SelectMany(
                das => das.amounts.DefaultIfEmpty(),
                (das, amount) => new { das.deck, amount })

            .Where(da => da.amount == default)
            .Select(da => da.deck);
    }


    private IQueryable<Deck> DecksWithoutWants(IQueryable<Deck> decks)
    {
        var wantsWithCard = _dbContext.Wants
            .Where(w => w.Card.Name == Card.Name
                && w.Location is Deck
                && (w.Location as Deck).OwnerId == Receiver.Id);

        return decks
            .GroupJoin( wantsWithCard,
                deck => deck.Id,
                want => want.LocationId,
                (deck, wants) => new { deck, wants })
            .SelectMany(
                dws => dws.wants.DefaultIfEmpty(),
                (dws, want) => new { dws.deck, want })
            
            .Where(dw => dw.want == default)
            .Select(dw => dw.deck);
    }


    private IQueryable<Deck> DecksWithoutSuggests(IQueryable<Deck> decks)
    {
        var suggestsWithCard = _dbContext.Suggestions
            .Where(s => s.Card.Name == Card.Name 
                && s.ReceiverId == Receiver.Id);

        return decks
            .GroupJoin( suggestsWithCard,
                deck => deck.Id,
                suggest => suggest.ToId,
                (deck, suggests) => new { deck, suggests })
            .SelectMany(
                dts => dts.suggests.DefaultIfEmpty(),
                (dts, suggest) => new { dts.deck, suggest })

            .Where(dt => dt.suggest == default)
            .Select(dt => dt.deck);
    }


    private IQueryable<Deck> DecksWithoutTrades(IQueryable<Deck> decks)
    {
        var tradesWithCard = _dbContext.Trades
            .Where(t => t.Card.Name == Card.Name && t.To.OwnerId == Receiver.Id);

        return decks
            .GroupJoin( tradesWithCard,
                deck => deck.Id,
                transfer => transfer.ToId,
                (deck, trades) => new { deck, trades })
            .SelectMany(
                dts => dts.trades.DefaultIfEmpty(),
                (dts, trade) => new { dts.deck, trade })

            .Where(dt => dt.trade == default)
            .Select(dt => dt.deck);
    }



    [BindProperty]
    public Suggestion Suggestion { get; set; }


    public async Task<IActionResult> OnPostSuggestAsync()
    {
        var validSuggest = await IsValidSuggestionAsync(Suggestion);

        if (!validSuggest)
        {
            return await OnPostUserAsync(Suggestion.ReceiverId);
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

        return RedirectToPage("Index");
    }


    private async Task<bool> IsValidSuggestionAsync(Suggestion suggestion)
    {
        if (!ModelState.IsValid)
        {
            return false;
        }

        _dbContext.Suggestions.Attach(Suggestion);

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