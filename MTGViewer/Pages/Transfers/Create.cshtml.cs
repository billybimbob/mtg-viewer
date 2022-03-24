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
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Transfers;


[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public class CreateModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly int _pageSize;

    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        PageSizes pageSizes,
        ILogger<CreateModel> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _pageSize = pageSizes.GetPageModelSize<CreateModel>();
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public DeckDetails Deck { get; private set; } = default!;

    public OffsetList<LocationCopy> Requests { get; private set; } = OffsetList<LocationCopy>.Empty;

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

        if (deck.HasTrades)
        {
            return RedirectToPage("Details", new { deck.Id });
        }

        // offset paging used since the amount of pages is unlikely 
        // to be that high

        var requests = await RequestMatches(deck)
            .PageBy(offset, _pageSize)
            .ToOffsetListAsync(cancel);

        if (requests.Offset.Current > requests.Offset.Total)
        {
            _logger.LogWarning("Invalid page offset {Offset}", offset);

            return RedirectToPage(new { offset = null as int? });
        }

        Deck = deck;
        Requests = requests;

        Cards = await DeckCardsAsync
            .Invoke(_dbContext, id, _pageSize)
            .ToListAsync(cancel);

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


    private IQueryable<LocationCopy> RequestMatches(DeckDetails deck)
    {
        var deckWants = _dbContext.Wants
            .Where(w => w.LocationId == deck.Id)
            .OrderBy(w => w.Card.Name)
                .ThenBy(w => w.Card.SetName)
                .ThenBy(w => w.Id);

        var possibleTargets = _dbContext.Users
            .Where(u => u.Id != deck.Owner.Id && !u.ResetRequested)
            .SelectMany(u => u.Decks)
            .SelectMany(d => d.Holds, (_, h) => h.Card.Name)
            .Distinct();

        return deckWants.Join(possibleTargets,
            w => w.Card.Name,
            name => name,
            (w, _) => new LocationCopy
            {
                Id = w.CardId,
                Name = w.Card.Name,
                SetName = w.Card.SetName,

                ManaCost = w.Card.ManaCost,
                Rarity = w.Card.Rarity,
                ImageUrl = w.Card.ImageUrl,

                Held = w.Copies
            });
    }


    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<DeckLink>> DeckCardsAsync
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


    private IQueryable<Trade> TradeRequests(DeckDetails deck)
    {
        var wantNames = _dbContext.Wants
            .Where(w => w.LocationId == deck.Id)
            .GroupBy(w => w.Card.Name,
                (Name, wants) =>
                    new { Name, Copies = wants.Sum(w => w.Copies) });

        // TODO: prioritize requesting from exact card matches

        return _dbContext.Users
            .Where(u => u.Id != deck.Owner.Id && !u.ResetRequested)
            .SelectMany(u => u.Decks)
            .SelectMany(d => d.Holds)
            .OrderBy(h => h.Id)

            // intentionally leave wants unbounded by target since
            // that cap will be handled later

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

        if (deck.HasTrades)
        {
            PostMessage = "Request is already sent";

            return RedirectToPage("Details", new { id });
        }

        var trades = await TradeRequests(deck).ToListAsync(cancel);

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
