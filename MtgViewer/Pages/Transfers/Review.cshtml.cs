using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Transfers;

[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public class ReviewModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public ReviewModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public DeckDetails Deck { get; private set; } = default!;

    public OffsetList<TradePreview> Trades { get; private set; } = OffsetList<TradePreview>.Empty;

    public IReadOnlyList<LocationLink> Cards { get; private set; } = Array.Empty<LocationLink>();

    public async Task<IActionResult> OnGetAsync(int id, int? offset, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Challenge();
        }

        var deck = await DeckAsync.Invoke(_dbContext, id, userId, cancel);

        if (deck is null)
        {
            return NotFound();
        }

        if (!deck.HasTrades)
        {
            return RedirectToPage("Index");
        }

        var cards = await DeckCardsAsync
            .Invoke(_dbContext, deck.Id, _pageSize.Current)
            .ToListAsync(cancel);

        if (!cards.Any())
        {
            return RedirectToPage("Index");
        }

        var trades = await ActiveTrades(deck)
            .PageBy(offset, _pageSize.Current)
            .ToOffsetListAsync(cancel);

        if (trades.Offset.Current > trades.Offset.Total)
        {
            return RedirectToPage(new { offset = null as int? });
        }

        Deck = deck;
        Trades = trades;
        Cards = cards;

        return Page();
    }

    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<DeckDetails?>> DeckAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, string userId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId
                    && d.OwnerId == userId
                    && d.TradesFrom.Any())

                .Select(d => new DeckDetails
                {
                    Id = d.Id,
                    Name = d.Name,
                    Color = d.Color,

                    Owner = new PlayerPreview
                    {
                        Id = d.OwnerId,
                        Name = d.Owner.Name
                    },

                    HeldCopies = d.Holds.Sum(h => h.Copies),
                    HasTrades = d.TradesFrom.Any()
                })

                .SingleOrDefault());

    private IQueryable<TradePreview> ActiveTrades(DeckDetails deck)
    {
        var deckHolds = _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .SelectMany(d => d.Holds)

            .OrderBy(h => h.Card.Name)
                .ThenBy(h => h.Card.SetName)
                .ThenBy(h => h.Id);

        var receivedTrades = _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .SelectMany(d => d.TradesFrom);

        // each join should be one-to-one because
        // holds are unique by Location and Card

        return deckHolds.Join(receivedTrades,
            h => h.CardId,
            t => t.CardId,
            (h, t) => new TradePreview
            {
                Id = t.Id,

                Card = new CardPreview
                {
                    Id = h.CardId,
                    Name = h.Card.Name,

                    ManaCost = h.Card.ManaCost,
                    ManaValue = h.Card.ManaValue,

                    SetName = h.Card.SetName,
                    Rarity = h.Card.Rarity,
                    ImageUrl = h.Card.ImageUrl,
                },

                Target = new DeckDetails
                {
                    Id = t.ToId,
                    Name = t.To.Name,
                    Color = t.To.Color,

                    Owner = new PlayerPreview
                    {
                        Id = t.To.OwnerId,
                        Name = t.To.Owner.Name
                    }
                },

                Copies = h.Copies < t.Copies ? h.Copies : t.Copies
            });
    }

    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<LocationLink>> DeckCardsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int id, int limit) =>

            dbContext.Decks
                .Where(d => d.Id == id)
                .SelectMany(d => d.Holds)

                .OrderBy(h => h.Card.Name)
                    .ThenBy(h => h.Card.SetName)
                    .ThenBy(h => h.CardId)

                .Take(limit)
                .Select(h => new LocationLink
                {
                    Id = h.CardId,
                    Name = h.Card.Name,
                    SetName = h.Card.SetName,
                    ManaCost = h.Card.ManaCost,

                    Held = h.Copies
                }));

    private record AcceptRequest(
        Trade Trade,
        WantNameGroup? ToWants,
        Hold FromHold);

    private record AcceptTargets(
        Hold ToHold,
        Want FromWant);

    public async Task<IActionResult> OnPostAcceptAsync(int tradeId, int amount, CancellationToken cancel)
    {
        var trade = await GetTradeAsync(tradeId, cancel);

        if (trade is null)
        {
            PostMessage = "Trade could not be found";

            return RedirectToPage();
        }

        var acceptRequest = GetAcceptRequest(trade);

        if (acceptRequest is null)
        {
            PostMessage = "Source Deck lacks the cards to complete the trade";

            return RedirectToPage();
        }

        ApplyAccept(acceptRequest, amount);

        await UpdateOtherTradesAsync(acceptRequest, cancel);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Trade successfully Applied";
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into error while Accepting";
        }

        return RedirectToPage();
    }

    private async Task<Trade?> GetTradeAsync(int id, CancellationToken cancel)
    {
        if (id == default)
        {
            return null;
        }

        var tradeCard = await _dbContext.Trades
            .Where(t => t.Id == id)
            .Select(t => t.Card)
            .SingleOrDefaultAsync(cancel);

        if (tradeCard == default)
        {
            return null;
        }

        return await TradeForReview(id, tradeCard)
            .SingleOrDefaultAsync(cancel);
    }

    private IQueryable<Trade> TradeForReview(int id, Card tradeCard)
    {
        string? userId = _userManager.GetUserId(User);

        return _dbContext.Trades
            .Where(t => t.Id == id && t.From.OwnerId == userId)

            .Include(t => t.To.Holds
                .Where(h => h.CardId == tradeCard.Id)
                .Take(1))

            .Include(t => t.To.Wants // unbounded: keep eye on
                .Where(w => w.Card.Name == tradeCard.Name))
                .ThenInclude(w => w.Card)

            .Include(t => t.From.Holds
                .Where(h => h.CardId == tradeCard.Id)
                .Take(1))

            .Include(t => t.From.Wants
                .Where(w => w.CardId == tradeCard.Id)
                .Take(1))

            .AsSplitQuery();
    }

    private static AcceptRequest? GetAcceptRequest(Trade trade)
    {
        bool tradeValid = trade.From.Holds
            .Select(h => h.CardId)
            .Contains(trade.CardId);

        if (!tradeValid)
        {
            return null;
        }

        var toWants = trade.To.Wants
            .Where(w => w.Card.Name == trade.Card.Name);

        return new AcceptRequest(
            Trade: trade,

            ToWants: toWants.Any()
                ? new WantNameGroup(toWants) : default,

            FromHold: trade.From.Holds
                .Single(h => h.CardId == trade.CardId));
    }

    private void ApplyAccept(AcceptRequest acceptRequest, int amount)
    {
        int acceptAmount = Math.Max(amount, 1);

        ModifyHoldsAndWants(acceptRequest, acceptAmount);

        var (trade, toWants, fromHold) = acceptRequest;

        if (fromHold.Copies == 0)
        {
            _dbContext.Holds.Remove(fromHold);
        }

        var finishedWants = toWants
            ?.Where(w => w.Copies == 0) ?? Enumerable.Empty<Want>();

        _dbContext.Wants.RemoveRange(finishedWants);
        _dbContext.Trades.Remove(trade);
    }

    private void ModifyHoldsAndWants(AcceptRequest acceptRequest, int acceptAmount)
    {
        var (trade, toWants, fromHold) = acceptRequest;

        if (toWants == default)
        {
            return;
        }

        var (toHold, fromWant) = GetAcceptTargets(acceptRequest);

        var exactWant = toWants
            .SingleOrDefault(w => w.CardId == trade.CardId);

        int change = new[] {
            acceptAmount, trade.Copies,
            toWants.Copies, fromHold.Copies }.Min();

        if (exactWant != default)
        {
            int exactChange = Math.Min(change, exactWant.Copies);
            int nonExactChange = change - exactChange;

            // exactRequest mod is also reflected in toWants
            exactWant.Copies -= exactChange;
            toWants.Copies -= nonExactChange;
        }
        else
        {
            toWants.Copies -= change;
        }

        toHold.Copies += change;
        fromHold.Copies -= change;
        fromWant.Copies += change;

        var newChange = new Change
        {
            Card = trade.Card,
            From = fromHold.Location,
            To = toHold.Location,

            Copies = change,
            Transaction = new Transaction()
        };

        _dbContext.Changes.Attach(newChange);
    }

    private AcceptTargets GetAcceptTargets(AcceptRequest request)
    {
        var trade = request.Trade;

        var toHold = trade.To.Holds
            .SingleOrDefault(h => h.CardId == trade.CardId);

        if (toHold is null)
        {
            toHold = new Hold
            {
                Card = trade.Card,
                Location = trade.To,
                Copies = 0
            };

            _dbContext.Holds.Attach(toHold);
        }

        var fromWant = trade.From.Wants
            .SingleOrDefault(w => w.CardId == trade.CardId);

        if (fromWant is null)
        {
            fromWant = new Want
            {
                Card = trade.Card,
                Location = trade.From,
                Copies = 0
            };

            _dbContext.Wants.Attach(fromWant);
        }

        return new AcceptTargets(toHold, fromWant);
    }

    private async Task UpdateOtherTradesAsync(AcceptRequest acceptRequest, CancellationToken cancel)
    {
        var (trade, toWants, _) = acceptRequest;

        if (toWants is null)
        {
            return;
        }

        // keep eye on, could possibly remove trades not started
        // by the user
        // current impl makes the assumption that trades are always
        // started by the owner of the To deck

        var remainingTrades = await _dbContext.Trades
            .Where(t => t.Id != trade.Id
                && t.ToId == trade.ToId
                && t.Card.Name == trade.Card.Name)
            .ToListAsync(cancel);

        if (!remainingTrades.Any())
        {
            return;
        }

        if (toWants.Copies == 0)
        {
            _dbContext.Trades.RemoveRange(remainingTrades);
            return;
        }

        foreach (var remaining in remainingTrades)
        {
            remaining.Copies = toWants.Copies;
        }
    }

    public async Task<IActionResult> OnPostRejectAsync(int tradeId, CancellationToken cancel)
    {
        if (tradeId == default)
        {
            PostMessage = "Trade is not specified";
            return RedirectToPage();
        }

        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Forbid();
        }

        var deckTrade = await _dbContext.Trades
            .SingleOrDefaultAsync(t =>
                t.Id == tradeId && t.From.OwnerId == userId, cancel);

        if (deckTrade == default)
        {
            PostMessage = "Trade could not be found";
            return RedirectToPage();
        }

        _dbContext.Trades.Remove(deckTrade);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);
            PostMessage = "Successfully rejected Trade";
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into error while rejecting";
        }

        return RedirectToPage();
    }
}
