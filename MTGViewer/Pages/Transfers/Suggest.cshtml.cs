using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    public string? PostMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string CardId { get; set; } = string.Empty;

    public Card Card { get; private set; } = null!;

    public PagedList<UserRef> Users { get; private set; } = PagedList<UserRef>.Empty;


    public UserRef? Receiver { get; private set; }

    public IReadOnlyList<Deck>? Decks { get; private set; }


    [BindProperty]
    public Suggestion? Suggestion { get; set; }



    public async Task<IActionResult> OnGetAsync(int? pageIndex, CancellationToken cancel)
    {
        var card = await _dbContext.Cards.FindAsync(new []{ CardId }, cancel);

        if (card is null)
        {
            return NotFound();
        }

        Card = card;

        Users = await UsersForSuggest(card)
            .ToPagedListAsync(_pageSize, pageIndex, cancel);

        return Page();
    }


    public IQueryable<UserRef> UsersForSuggest(Card card)
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
            .OrderBy(u => u.Name)
            .AsNoTracking();
    }



    public async Task<IActionResult> OnGetUserAsync(string userId, CancellationToken cancel)
    {
        if (userId == null)
        {
            return RedirectToPage();
        }

        var card = await _dbContext.Cards.FindAsync(new []{ CardId }, cancel);

        if (card is null)
        {
            return NotFound();
        }

        var currentUserId = _userManager.GetUserId(User);
        
        if (currentUserId == userId)
        {
            return NotFound();
        }

        var receiver = await _dbContext.Users.FindAsync(new []{ userId }, cancel);

        if (receiver is null)
        {
            return NotFound();
        }

        Decks = await DecksForSuggest(card, receiver)
            .ToListAsync(cancel);

        Card = card;
        Receiver = receiver;

        return Page();
    }


    private IQueryable<Deck> DecksForSuggest(Card card, UserRef receiver)
    {
        IQueryable<Deck> userDecks;

        userDecks = _dbContext.Decks
            .Where(l => l.OwnerId == receiver.Id);

        userDecks = DecksWithoutAmounts(userDecks, card, receiver);
        userDecks = DecksWithoutWants(userDecks, card, receiver);

        userDecks = DecksWithoutSuggests(userDecks, card, receiver);
        userDecks = DecksWithoutTrades(userDecks, card, receiver);

        // unbounded, keep eye on
        return userDecks
            .OrderBy(d => d.Name)
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    private IQueryable<Deck> DecksWithoutAmounts(
        IQueryable<Deck> decks, Card card, UserRef receiver)
    {
        var userCardAmounts = _dbContext.Amounts
            .Where(ca => ca.Card.Name == card.Name
                && ca.Location is Deck
                && (ca.Location as Deck)!.OwnerId == receiver.Id);

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


    private IQueryable<Deck> DecksWithoutWants(
        IQueryable<Deck> decks, Card card, UserRef receiver)
    {
        var wantsWithCard = _dbContext.Wants
            .Where(w => w.Card.Name == card.Name
                && w.Location is Deck
                && (w.Location as Deck)!.OwnerId == receiver.Id);

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


    private IQueryable<Deck> DecksWithoutSuggests(
        IQueryable<Deck> decks, Card card, UserRef receiver)
    {
        var suggestsWithCard = _dbContext.Suggestions
            .Where(s => s.Card.Name == card.Name 
                && s.ReceiverId == receiver.Id);

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


    private IQueryable<Deck> DecksWithoutTrades(
        IQueryable<Deck> decks, Card card, UserRef receiver)
    {
        var tradesWithCard = _dbContext.Trades
            .Where(t => t.Card.Name == card.Name && t.To.OwnerId == receiver.Id);

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



    public async Task<IActionResult> OnPostSuggestAsync(CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
        var validSuggest = await IsValidSuggestionAsync(Suggestion, userId, cancel);

        if (!validSuggest)
        {
            return RedirectToPage(
                "Suggest", "User", 
                new { CardId, userId = Suggestion?.ReceiverId });
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancel);
            PostMessage = "Suggestion Successfully Created";
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into issue while creating Suggestion";
        }

        return RedirectToPage("Index");
    }


    private async Task<bool> IsValidSuggestionAsync(
        Suggestion? suggestion, string userId, CancellationToken cancel)
    {
        if (suggestion is null)
        {
            return false;
        }

        if (suggestion.ReceiverId is null || suggestion.CardId is null)
        {
            return false;
        }

        if (suggestion.ReceiverId == userId)
        {
            PostMessage = "Suggestion cannot be sent to yourself";
            return false;
        }

        var entry = _dbContext.Suggestions.Attach(suggestion);

        await entry
            .Reference(s => s.Card)
            .LoadAsync(cancel);

        await entry
            .Reference(s => s.Receiver)
            .LoadAsync(cancel);

        if (suggestion.ToId is not null)
        {
            await entry
                .Reference(s => s.To)
                .LoadAsync(cancel);
        }

        ModelState.ClearValidationState(nameof(Suggestion)); 

        if (!TryValidateModel(suggestion, nameof(Suggestion)))
        {
            PostMessage = "Suggestion is not valid";
            return false;
        }

        bool suggestPrior = await _dbContext.Suggestions
            .AnyAsync(t => t.ReceiverId == suggestion.ReceiverId
                && t.CardId == suggestion.CardId
                && t.ToId == suggestion.ToId, cancel);

        if (suggestPrior)
        {
            PostMessage = "Suggestion is redundant";
            return false;
        }

        if (suggestion.ToId is null)
        {
            return true;
        }

        await _dbContext.Entry(suggestion.To!)
            .Collection(t => t.Cards)
            .LoadAsync(cancel);

        var suggestInDeck = suggestion.To!.Cards
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