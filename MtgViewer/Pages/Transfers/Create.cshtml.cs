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

namespace MtgViewer.Pages.Transfers;

[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public class CreateModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize,
        ILogger<CreateModel> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSize;
        _logger = logger;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public DeckDetails Deck { get; private set; } = default!;

    public OffsetList<CardCopy> Requests { get; private set; } = OffsetList.Empty<CardCopy>();

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

        if (deck.HasTrades)
        {
            return RedirectToPage("Details", new { deck.Id });
        }

        // offset paging used since the amount of pages is unlikely
        // to be that high

        var requests = await RequestMatches(deck)
            .PageBy(offset, _pageSize.Current)
            .ToOffsetListAsync(cancel);

        if (requests.Offset.Current > requests.Offset.Total)
        {
            _logger.LogWarning("Invalid page offset {Offset}", offset);

            return RedirectToPage(new { offset = null as int? });
        }

        Deck = deck;

        Requests = requests;

        Cards = await DeckCardsAsync
            .Invoke(_dbContext, id, _pageSize.Current)
            .ToListAsync(cancel);

        return Page();
    }

    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<DeckDetails?>> DeckAsync
        = EF.CompileAsyncQuery((CardDbContext db, int deck, string owner, CancellationToken _)
            => db.Decks
                .Where(d => d.Id == deck && d.OwnerId == owner && d.Wants.Any())

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

    private IQueryable<CardCopy> RequestMatches(DeckDetails deck)
    {
        var deckWants = _dbContext.Wants
            .Where(w => w.LocationId == deck.Id)
            .OrderBy(w => w.Card.Name)
                .ThenBy(w => w.Card.SetName)
                .ThenBy(w => w.Id);

        var possibleTargets = _dbContext.Players
            .Where(p => p.Id != deck.Owner.Id && !p.ResetRequested)
            .SelectMany(p => p.Decks)
            .SelectMany(d => d.Holds, (_, h) => h.Card.Name)
            .Distinct();

        return deckWants.Join(possibleTargets,
            w => w.Card.Name,
            name => name,
            (w, _) => new CardCopy
            {
                Id = w.CardId,
                Name = w.Card.Name,

                ManaCost = w.Card.ManaCost,
                ManaValue = w.Card.ManaValue,

                SetName = w.Card.SetName,
                Rarity = w.Card.Rarity,
                ImageUrl = w.Card.ImageUrl,

                Held = w.Copies
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

    private IQueryable<Trade> TradeMatches(DeckDetails deck)
    {
        var wantNames = _dbContext.Wants
            .Where(w => w.LocationId == deck.Id)
            .GroupBy(w => w.Card.Name,
                (Name, wants) => new { Name, Copies = wants.Sum(w => w.Copies) });

        // TODO: prioritize requesting from exact card matches

        // intentionally leave wants unbounded by target since
        // that cap will be handled later

        // keep eye on, this query potentially be expensive, and is also unbounded

        return _dbContext.Players
            .Where(p => p.Id != deck.Owner.Id && !p.ResetRequested)
            .SelectMany(p => p.Decks)
            .SelectMany(d => d.Holds)
            .OrderBy(h => h.Id)

            .Join(wantNames,
                h => h.Card.Name,
                want => want.Name,
                (target, want) => new Trade
                {
                    ToId = deck.Id,
                    FromId = target.LocationId,
                    Card = target.Card,
                    Copies = want.Copies
                });
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
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

        if (deck.HasTrades)
        {
            PostMessage = "Request is already sent";

            return RedirectToPage("Details", new { id });
        }

        var trades = await TradeMatches(deck).ToListAsync(cancel);

        if (!trades.Any())
        {
            PostMessage = "There are no possible decks to trade with";

            return RedirectToPage("Index");
        }

        _dbContext.Trades.AttachRange(trades);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Request was successfully sent";

            return RedirectToPage("Details", new { id });
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into issue while trying to send request";

            return RedirectToPage("Index");
        }
    }

}
