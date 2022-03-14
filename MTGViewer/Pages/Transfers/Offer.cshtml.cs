using System;
using System.Linq;
using System.Paging;
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
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public class OfferModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly int _pageSize;

    public OfferModel(
        CardDbContext dbContext, 
        UserManager<CardUser> userManager,
        PageSizes pageSizes)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _pageSize = pageSizes.GetPageModelSize<OfferModel>();
    }


    [TempData]
    public string? PostMessage { get; set; }


    public CardPreview Card { get; private set; } = default!;

    public UserRef Receiver { get; private set; } = default!;

    public OffsetList<Deck> Decks { get; private set; } = OffsetList<Deck>.Empty;


    [BindProperty]
    public Suggestion? Suggestion { get; set; }


    public async Task<IActionResult> OnGetAsync(
        string id, 
        string receiverId,
        int? offset,
        CancellationToken cancel)
    {
        var card = await CardAsync.Invoke(_dbContext, id, cancel);

        if (card is null)
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User);
        if (userId == receiverId)
        {
            return NotFound();
        }

        var receiver = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == receiverId, cancel);

        if (receiver is null)
        {
            return NotFound();
        }

        var decks = await DecksForSuggest(card, receiver)
            .PageBy(offset, _pageSize)
            .ToOffsetListAsync(cancel);

        if (decks.Offset.Current > decks.Offset.Total)
        {
            return RedirectToPage(new { offset = null as int? });
        }

        Card = card;
        Receiver = receiver;
        Decks = decks;

        return Page();
    }



    private static readonly Func<CardDbContext, string, CancellationToken, Task<CardPreview?>> CardAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, CancellationToken _) =>
            dbContext.Cards
                .Select(c => new CardPreview
                {
                    Id = c.Id,
                    Name = c.Name,
                    SetName = c.SetName,

                    ManaCost = c.ManaCost,
                    Rarity = c.Rarity,
                    ImageUrl = c.ImageUrl
                })
                .SingleOrDefault(c => c.Id == cardId));


    private IQueryable<Deck> DecksForSuggest(CardPreview card, UserRef receiver)
    {
        var userDecks = _dbContext.Decks
            .Where(l => l.OwnerId == receiver.Id)
            .OrderBy(d => d.Name)
            .AsQueryable();

        userDecks = DecksWithoutAmounts(userDecks, card, receiver);
        userDecks = DecksWithoutWants(userDecks, card, receiver);

        userDecks = DecksWithoutSuggests(userDecks, card, receiver);
        userDecks = DecksWithoutTrades(userDecks, card, receiver);

        return userDecks;
    }


    private IQueryable<Deck> DecksWithoutAmounts(
        IQueryable<Deck> decks,
        CardPreview card,
        UserRef receiver)
    {
        var userCardAmounts = _dbContext.Amounts
            .Where(a => a.Card.Name == card.Name
                && a.Location is Deck
                && (a.Location as Deck)!.OwnerId == receiver.Id);

        return decks 
            .GroupJoin( userCardAmounts,
                deck => deck.Id,
                amount => amount.LocationId,
                (Deck, Amounts) => new { Deck, Amounts })
            .SelectMany(
                das => das.Amounts.DefaultIfEmpty(),
                (das, Amount) => new { das.Deck, Amount })

            .Where(da => da.Amount == default)
            .Select(da => da.Deck);
    }


    private IQueryable<Deck> DecksWithoutWants(
        IQueryable<Deck> decks,
        CardPreview card,
        UserRef receiver)
    {
        var wantsWithCard = _dbContext.Wants
            .Where(w => w.Card.Name == card.Name
                && w.Location is Deck
                && (w.Location as Deck)!.OwnerId == receiver.Id);

        return decks
            .GroupJoin( wantsWithCard,
                deck => deck.Id,
                want => want.LocationId,
                (Deck, Wants) => new { Deck, Wants })
            .SelectMany(
                dws => dws.Wants.DefaultIfEmpty(),
                (dws, Want) => new { dws.Deck, Want })
            
            .Where(dw => dw.Want == default)
            .Select(dw => dw.Deck);
    }


    private IQueryable<Deck> DecksWithoutSuggests(
        IQueryable<Deck> decks,
        CardPreview card,
        UserRef receiver)
    {
        var suggestsWithCard = _dbContext.Suggestions
            .Where(s => s.Card.Name == card.Name 
                && s.ReceiverId == receiver.Id);

        return decks
            .GroupJoin( suggestsWithCard,
                deck => deck.Id,
                suggest => suggest.ToId,
                (Deck, Suggests) => new { Deck, Suggests })
            .SelectMany(
                dts => dts.Suggests.DefaultIfEmpty(),
                (dts, Suggest) => new { dts.Deck, Suggest })

            .Where(ds => ds.Suggest == default)
            .Select(ds => ds.Deck);
    }


    private IQueryable<Deck> DecksWithoutTrades(
        IQueryable<Deck> decks,
        CardPreview card,
        UserRef receiver)
    {
        var tradesWithCard = _dbContext.Trades
            .Where(t => t.Card.Name == card.Name && t.To.OwnerId == receiver.Id);

        return decks
            .GroupJoin( tradesWithCard,
                deck => deck.Id,
                transfer => transfer.ToId,
                (Deck, Trades) => new { Deck, Trades })
            .SelectMany(
                dts => dts.Trades.DefaultIfEmpty(),
                (dts, Trade) => new { dts.Deck, Trade })

            .Where(dt => dt.Trade == default)
            .Select(dt => dt.Deck);
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


    private async Task<bool> IsValidSuggestionAsync(Suggestion? suggestion, CancellationToken cancel)
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