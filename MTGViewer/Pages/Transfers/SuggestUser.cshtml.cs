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

namespace MTGViewer.Pages.Transfers;


[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public class SuggestUserModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;

    public SuggestUserModel(
        CardDbContext dbContext, UserManager<CardUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }


    [TempData]
    public string? PostMessage { get; set; }


    public Card Card { get; private set; } = default!;

    public UserRef Receiver { get; private set; } = default!;

    public IReadOnlyList<Deck> Decks { get; private set; } = Array.Empty<Deck>();


    [BindProperty]
    public Suggestion? Suggestion { get; set; }


    public async Task<IActionResult> OnGetAsync(string cardId, string receiverId, CancellationToken cancel)
    {
        if (cardId == null || receiverId == null)
        {
            return NotFound();
        }

        var card = await _dbContext.Cards.FindAsync(new []{ cardId }, cancel);
        if (card is null)
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User);
        if (userId == receiverId)
        {
            return NotFound();
        }

        var receiver = await _dbContext.Users.FindAsync(new []{ receiverId }, cancel);
        if (receiver is null)
        {
            return NotFound();
        }

        Card = card;

        Receiver = receiver;

        Decks = await DecksForSuggest(card, receiver)
            .ToListAsync(cancel);

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
            .AsNoTrackingWithIdentityResolution();
    }


    private IQueryable<Deck> DecksWithoutAmounts(
        IQueryable<Deck> decks, Card card, UserRef receiver)
    {
        var userCardAmounts = _dbContext.Amounts
            .Where(a => a.Card.Name == card.Name
                && a.Location is Deck
                && (a.Location as Deck)!.OwnerId == receiver.Id);

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



    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        var validSuggest = await IsValidSuggestionAsync(Suggestion, cancel);

        if (!validSuggest)
        {
            return RedirectToPage();
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
        Suggestion? suggestion, CancellationToken cancel)
    {
        string userId = _userManager.GetUserId(User);

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

            if (suggestion.To!.OwnerId != suggestion.ReceiverId)
            {
                PostMessage = "Suggestion To Deck is not valid";
                return false;
            }
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