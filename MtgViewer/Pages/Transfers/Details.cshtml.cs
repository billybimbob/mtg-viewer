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
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Transfers;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public class DetailsModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public DetailsModel(
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

    public OffsetList<TradePreview> Trades { get; private set; } = OffsetList.Empty<TradePreview>();

    public IReadOnlyList<DeckLink> Cards { get; private set; } = Array.Empty<DeckLink>();

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
            return RedirectToPage("Create", new { deck.Id });
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

        Cards = await DeckCardsAsync
            .Invoke(_dbContext, deck.Id, _pageSize.Current)
            .ToListAsync(cancel);

        return Page();
    }

    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<DeckDetails?>> DeckAsync
        = EF.CompileAsyncQuery((CardDbContext db, int deck, string owner, CancellationToken _)
            => db.Decks
                .Where(d =>
                    d.Id == deck && d.OwnerId == owner && d.Wants.Any())

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
                    WantCopies = d.Wants.Sum(w => w.Copies),

                    HasTrades = d.TradesTo.Any()
                })

                .SingleOrDefault());

    private IQueryable<TradePreview> ActiveTrades(DeckDetails deck)
    {
        var deckTrades = _dbContext.Decks
            .Where(d => d.Id == deck.Id)
            .SelectMany(d => d.TradesTo)

            .OrderBy(t => t.Card.Name)
                .ThenBy(t => t.Card.SetName)
                .ThenBy(t => t.Id);

        // each join should be one-to-one match because
        // holds are unique by Location and Card

        return deckTrades
            .Join(_dbContext.Holds,
                t => new { LocationId = t.FromId, t.CardId },
                h => new { h.LocationId, h.CardId },
                (t, h) => new TradePreview
                {
                    Id = t.Id,

                    Card = new CardPreview
                    {
                        Id = t.CardId,
                        Name = t.Card.Name,

                        ManaCost = t.Card.ManaCost,
                        ManaValue = t.Card.ManaValue,

                        SetName = t.Card.SetName,
                        Rarity = t.Card.Rarity,
                        ImageUrl = t.Card.ImageUrl,
                    },

                    Target = new DeckDetails
                    {
                        Id = t.FromId,
                        Name = t.From.Name,
                        Color = t.From.Color,

                        Owner = new PlayerPreview
                        {
                            Id = t.From.OwnerId,
                            Name = t.From.Owner.Name
                        }
                    },

                    Copies = t.Copies > h.Copies ? t.Copies : h.Copies
                });
    }

    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<DeckLink>> DeckCardsAsync
        = EF.CompileAsyncQuery((CardDbContext db, int deck, int limit)
            => db.Cards
                .Where(c => c.Holds.Any(h => h.LocationId == deck)
                    || c.Wants.Any(w => w.LocationId == deck))

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
                        .Where(h => h.LocationId == deck)
                        .Sum(h => h.Copies),

                    Want = c.Wants
                        .Where(w => w.LocationId == deck)
                        .Sum(w => w.Copies)
                }));

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Forbid();
        }

        // keep eye on, could possibly remove trades not started by the user
        // makes the assumption that trades are always started by the owner of the To deck

        var trades = await _dbContext.Decks
            .Where(d => d.Id == id && d.OwnerId == userId)
            .SelectMany(d => d.TradesTo)
            .ToListAsync(cancel); // unbounded, keep eye on

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
