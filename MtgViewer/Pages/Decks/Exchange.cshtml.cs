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
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Decks;

[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public class ExchangeModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;
    private readonly ILogger<ExchangeModel> _logger;

    public ExchangeModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize,
        ILogger<ExchangeModel> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSize;
        _logger = logger;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public ExchangePreview Deck { get; private set; } = default!;

    public OffsetList<LocationCopy> Matches { get; private set; } = OffsetList.Empty<LocationCopy>();

    public async Task<IActionResult> OnGetAsync(int id, int? offset, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var deck = await ExchangePreviewAsync
            .Invoke(_dbContext, id, userId, _pageSize.Current, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (!deck.HasWants && !deck.Givebacks.Any())
        {
            PostMessage = $"There are no pending requests for {deck.Name}";

            return RedirectToPage("Details", new { id });
        }

        var matches = await WantTargets(deck)
            .PageBy(offset, _pageSize.Current)
            .ToOffsetListAsync(cancel);

        if (matches.Offset.Current > matches.Offset.Total)
        {
            return RedirectToPage(new { offset = null as int? });
        }

        Deck = deck;
        Matches = matches;

        return Page();
    }

    private static readonly Func<CardDbContext, int, string, int, CancellationToken, Task<ExchangePreview?>> ExchangePreviewAsync
        = EF.CompileAsyncQuery(
            (CardDbContext dbContext, int deckId, string userId, int limit, CancellationToken _) =>

            dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId && !d.TradesTo.Any())
                .Select(d => new ExchangePreview
                {
                    Id = d.Id,
                    Name = d.Name,
                    HasWants = d.Wants.Any(),

                    Givebacks = d.Givebacks
                        .OrderBy(g => g.Card.Name)
                            .ThenBy(g => g.Card.SetName)
                            .ThenBy(g => g.Id)

                        .Take(limit)
                        .Select(g => new LocationCopy
                        {
                            Id = g.CardId,
                            Name = g.Card.Name,

                            ManaCost = g.Card.ManaCost,
                            ManaValue = g.Card.ManaValue,

                            SetName = g.Card.SetName,
                            Rarity = g.Card.Rarity,
                            ImageUrl = g.Card.ImageUrl,

                            Held = g.Copies
                        })
                })
                .SingleOrDefault());

    private IQueryable<LocationCopy> WantTargets(ExchangePreview deck)
    {
        var deckWants = _dbContext.Wants
            .Where(w => w.LocationId == deck.Id)
            .Select(w => w.Card.Name)
            .Distinct();

        var totalMatches = _dbContext.Holds
            .Where(h => h.Location is Box || h.Location is Excess)
            .Join(deckWants,
                h => h.Card.Name, name => name,
                (hold, _) => hold)
            .GroupBy(
                hold => hold.CardId,
                (CardId, holds) => new
                {
                    CardId,
                    Copies = holds.Sum(h => h.Copies)
                });

        return _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Join(totalMatches,
                c => c.Id,
                t => t.CardId,
                (c, t) => new LocationCopy
                {
                    Id = c.Id,
                    Name = c.Name,

                    ManaCost = c.ManaCost,
                    ManaValue = c.ManaValue,

                    SetName = c.SetName,
                    Rarity = c.Rarity,
                    ImageUrl = c.ImageUrl,

                    Held = t.Copies
                });
    }

    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<Deck?>> DeckForExchangeAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, string userId, CancellationToken _) =>
            dbContext.Decks
                .Include(d => d.Holds) // unbounded: keep eye one
                    .ThenInclude(h => h.Card)

                .Include(d => d.Wants) // unbounded: keep eye one
                    .ThenInclude(w => w.Card)

                .Include(d => d.Givebacks) // unbounded: keep eye one
                    .ThenInclude(g => g.Card)

                .Include(d => d.TradesFrom) // unbounded, keep eye on

                .OrderBy(d => d.Id)
                .AsSplitQuery()
                .SingleOrDefault(d =>
                    d.Id == deckId && d.OwnerId == userId && !d.TradesTo.Any()));

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var deck = await DeckForExchangeAsync
            .Invoke(_dbContext, id, userId, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        await ApplyChangesAsync(deck, cancel);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = deck.Wants.Any() || deck.Givebacks.Any()
                ? "Successfully exchanged requests, but not all could be fullfilled"
                : "Successfully exchanged all card requests";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("Ran into db error {Error}", e);

            PostMessage = "Ran into issue while trying to exchange";
        }

        return RedirectToPage("/Transactions/Index", new { id });
    }

    private async Task ApplyChangesAsync(Deck deck, CancellationToken cancel)
    {
        ApplyExchangeOverlap(deck);

        await _dbContext.ExchangeAsync(deck, cancel);

        var emptyTrades = deck.Holds
            .Where(h => h.Copies == 0)
            .Join(deck.TradesFrom,
                h => h.CardId,
                t => t.CardId,
                (_, trade) => trade);

        _dbContext.Trades.RemoveRange(emptyTrades);
    }

    private static void ApplyExchangeOverlap(Deck deck)
    {
        var exactMatches = deck.Wants
            .Join(deck.Givebacks,
                w => w.CardId, g => g.CardId,
                (want, give) => (want, give));

        foreach (var (want, give) in exactMatches)
        {
            int match = Math.Min(want.Copies, give.Copies);

            want.Copies -= match;
            give.Copies -= match;
        }

        var nameMatches = deck.Wants
            .Join(deck.Givebacks,
                w => w.Card.Name, g => g.Card.Name,
                (want, give) => (want, give));

        foreach (var (want, give) in exactMatches)
        {
            int match = Math.Min(want.Copies, give.Copies);

            want.Copies -= match;
            give.Copies -= match;
        }
    }
}
