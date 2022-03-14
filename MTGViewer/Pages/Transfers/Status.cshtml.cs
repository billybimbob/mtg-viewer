using System;
using System.Collections.Generic;
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
public class StatusModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly int _pageSize;

    public StatusModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSizes pageSizes)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSizes.GetPageModelSize<StatusModel>();
    }


    [TempData]
    public string? PostMessage { get; set; }

    public DeckDetails Deck { get; private set; } = default!;

    public OffsetList<TradePreview> Trades { get; private set; } = OffsetList<TradePreview>.Empty;

    public IReadOnlyList<DeckLink> Cards { get; private set; } = Array.Empty<DeckLink>();


    public async Task<IActionResult> OnGetAsync(int id, int? offset, CancellationToken cancel)
    {
        if (id == default)
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Challenge();
        }

        var deck = await DeckAsync.Invoke(_dbContext, id, userId, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (!deck.HasTrades)
        {
            return RedirectToPage("Request", new { id });
        }

        var trades = await CappedTrades(deck)
            .PageBy(offset, _pageSize)
            .ToOffsetListAsync(cancel);

        if (!trades.Any())
        {
            return RedirectToPage(new { offset = null as int? });
        }

        Deck = deck;

        Trades = trades;

        Cards = await DeckCards
            .Invoke(_dbContext, id, _pageSize)
            .ToListAsync();

        return Page();
    }


    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<DeckDetails?>> DeckAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, string userId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId
                    && d.OwnerId == userId
                    && d.Wants.Any())

                .Select(d => new DeckDetails
                {
                    Id = d.Id,
                    Name = d.Name,
                    Color = d.Color,

                    Owner = new OwnerPreview
                    {
                        Id = d.OwnerId,
                        Name = d.Owner.Name
                    },

                    AmountCopies = d.Cards.Sum(a => a.Copies),
                    WantCopies = d.Wants.Sum(w => w.Copies),
                    HasTrades = d.TradesTo.Any()
                })
                .SingleOrDefault());


    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<DeckLink>> DeckCards
        = EF.CompileAsyncQuery((CardDbContext dbContext, int id, int limit) =>

            dbContext.Cards
                .Where(c => c.Amounts.Any(a => a.LocationId == id)
                    || c.Wants.Any(w => w.LocationId == id))

                .OrderBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id)

                .Take(limit)
                .Select(c => new DeckLink
                {
                    Id = c.Id,
                    Name = c.Name,
                    SetName = c.SetName,
                    ManaCost = c.ManaCost,

                    Held = c.Amounts
                        .Where(a => a.LocationId == id)
                        .Sum(a => a.Copies),

                    Want = c.Wants
                        .Where(w => w.LocationId == id)
                        .Sum(w => w.Copies)
                }));


    private IQueryable<TradePreview> CappedTrades(DeckDetails deck)
    {
        var deckTrades = _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .SelectMany(d => d.TradesTo)

            .OrderBy(t => t.Card.Name)
                .ThenBy(t => t.Card.SetName)
                .ThenBy(t => t.Id);

        return deckTrades
            // left join, where each match is unique
            .GroupJoin( _dbContext.Amounts,
                t => new { LocationId = t.FromId, t.CardId },
                a => new { a.LocationId, a.CardId },
                (Trade, Amounts) => new { Trade, Amounts })

            .SelectMany(ta => ta.Amounts.DefaultIfEmpty(),
                (ta, a) => new TradePreview
                {
                    Card = new CardPreview
                    {
                        Id = ta.Trade.CardId,
                        Name = ta.Trade.Card.Name,
                        SetName = ta.Trade.Card.SetName,

                        ManaCost = ta.Trade.Card.ManaCost,
                        Rarity = ta.Trade.Card.Rarity,
                        ImageUrl = ta.Trade.Card.ImageUrl,
                    },

                    Target = new DeckTarget
                    {
                        Id = ta.Trade.FromId,
                        Name = ta.Trade.From.Name,
                        Color = ta.Trade.From.Color,

                        Owner = new OwnerPreview
                        {
                            Id = ta.Trade.From.OwnerId,
                            Name = ta.Trade.From.Owner.Name
                        }
                    },

                    Copies = a == null ? 0 : a.Copies
                });
    }



    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Forbid();
        }

        // keep eye on, could possibly remove trades not started by the user
        // makes the assumption that trades are always started by the owner of the To deck

        var trades = await _dbContext.Decks
            .Where(d => d.Id == id && d.OwnerId == userId)
            .SelectMany(d => d.TradesTo)
            .ToListAsync(cancel);

        if (!trades.Any())
        {
            PostMessage = "No trades were found";
            return RedirectToPage("Index");
        }

        _dbContext.Trades.RemoveRange(trades);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);
            PostMessage = "Successfully cancelled requests";
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into error while cancelling";
        }

        return RedirectToPage("Index");
    }
}