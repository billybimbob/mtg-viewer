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
public class DetailsModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly int _pageSize;

    public DetailsModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSizes pageSizes)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSizes.GetPageModelSize<DetailsModel>();
    }


    [TempData]
    public string? PostMessage { get; set; }

    public DeckDetails Deck { get; private set; } = default!;

    public OffsetList<TradePreview> Trades { get; private set; } = OffsetList<TradePreview>.Empty;

    public IReadOnlyList<DeckLink> Cards { get; private set; } = Array.Empty<DeckLink>();


    public async Task<IActionResult> OnGetAsync(int id, int? offset, CancellationToken cancel)
    {
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
            return RedirectToPage("Create", new { id });
        }

        var trades = await CappedTrades(deck)
            .PageBy(offset, _pageSize)
            .ToOffsetListAsync(cancel);

        if (trades.Offset.Current > trades.Offset.Total)
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

                    HeldCopies = d.Holds.Sum(h => h.Copies),
                    WantCopies = d.Wants.Sum(w => w.Copies),

                    HasTrades = d.TradesTo.Any()
                })
                .SingleOrDefault());


    private IQueryable<TradePreview> CappedTrades(DeckDetails deck)
    {
        var deckTrades = _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .SelectMany(d => d.TradesTo)

            .OrderBy(t => t.Card.Name)
                .ThenBy(t => t.Card.SetName)
                .ThenBy(t => t.Id);

        return deckTrades
            // left join, where each match is unique because
            // each hold is unique by Location and Card
            .GroupJoin( _dbContext.Holds,
                t => new { LocationId = t.FromId, t.CardId },
                h => new { h.LocationId, h.CardId },
                (Trade, Holds) => new { Trade, Holds })

            .SelectMany(th => th.Holds.DefaultIfEmpty(),
                (th, h) => new TradePreview
                {
                    Card = new CardPreview
                    {
                        Id = th.Trade.CardId,
                        Name = th.Trade.Card.Name,
                        SetName = th.Trade.Card.SetName,

                        ManaCost = th.Trade.Card.ManaCost,
                        Rarity = th.Trade.Card.Rarity,
                        ImageUrl = th.Trade.Card.ImageUrl,
                    },

                    Target = new DeckDetails
                    {
                        Id = th.Trade.FromId,
                        Name = th.Trade.From.Name,
                        Color = th.Trade.From.Color,

                        Owner = new OwnerPreview
                        {
                            Id = th.Trade.From.OwnerId,
                            Name = th.Trade.From.Owner.Name
                        }
                    },

                    Copies = h == null ? 0 : h.Copies
                });
    }


    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<DeckLink>> DeckCards
        = EF.CompileAsyncQuery((CardDbContext dbContext, int id, int limit) =>

            dbContext.Cards
                .Where(c => c.Holds.Any(h => h.LocationId == id)
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

                    Held = c.Holds
                        .Where(h => h.LocationId == id)
                        .Sum(h => h.Copies),

                    Want = c.Wants
                        .Where(w => w.LocationId == id)
                        .Sum(w => w.Copies)
                }));



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