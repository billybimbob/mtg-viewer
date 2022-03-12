using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Decks;


[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public class ExchangeModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly int _pageSize;
    private readonly ILogger<ExchangeModel> _logger;

    public ExchangeModel(
        CardDbContext dbContext,
        UserManager<CardUser> userManager,
        PageSizes pageSizes,
        ILogger<ExchangeModel> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _pageSize = pageSizes.GetPageModelSize<ExchangeModel>();
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public DeckDetails Deck { get; private set; } = default!;

    public IReadOnlyList<DeckLink> Cards { get; private set; } = Array.Empty<DeckLink>();

    public bool HasPendings { get; private set; }


    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == default)
        {
            return NotFound();
        }

        var deck = await DeckDetailsAsync.Invoke(_dbContext, id, userId, cancel);
        if (deck == default)
        {
            return NotFound();
        }

        Deck = deck;

        Cards = await DeckCardsAsync
            .Invoke(_dbContext, id, _pageSize)
            .ToListAsync(cancel);

        HasPendings = await HasPendingsAsync(deck, cancel);

        return Page();
    }


    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<DeckDetails?>> DeckDetailsAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, string userId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId && !d.TradesTo.Any())
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

                    AmountTotal = d.Cards.Sum(a => a.Copies),
                    WantTotal = d.Wants.Sum(w => w.Copies),
                    GiveBackTotal = d.GiveBacks.Sum(g => g.Copies),

                    AnyTrades = d.TradesTo.Any() || d.TradesFrom.Any()
                })
                .SingleOrDefault());


    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<DeckLink>> DeckCardsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int id, int limit) =>

            dbContext.Cards
                .Where(c => c.Amounts.Any(a => a.LocationId == id)
                    || c.Wants.Any(w => w.LocationId == id)
                    || c.GiveBacks.Any(g => g.LocationId == id))

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
                        .Sum(w => w.Copies),

                    Returning = c.GiveBacks
                        .Where(g => g.LocationId == id)
                        .Sum(g => g.Copies),
                }));


    private async ValueTask<bool> HasPendingsAsync(DeckDetails deck, CancellationToken cancel)
    {
        if (deck.GiveBackTotal > 0)
        {
            return true;
        }

        if (deck.WantTotal == 0)
        {
            return false;
        }

        var wants = _dbContext.Wants
            .Where(w => w.LocationId == deck.Id)
            .Select(w => w.Card.Name);

        return await _dbContext.Amounts
            .Where(a => (a.Location is Box || a.Location is Excess))
            .Select(a => a.Card.Name)
            .Join(wants, a => a, w => w, (_, _) => true)
            .AnyAsync(cancel);
    }


    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<Deck?>> DeckForExchangeAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, string userId, CancellationToken _) =>
            dbContext.Decks
                .Include(d => d.Cards) // unbounded: keep eye one
                    .ThenInclude(a => a.Card)

                .Include(d => d.Wants) // unbounded: keep eye one
                    .ThenInclude(w => w.Card)

                .Include(d => d.GiveBacks) // unbounded: keep eye one
                    .ThenInclude(g => g.Card)

                .Include(d => d.TradesFrom) // unbounded, keep eye on
                .OrderBy(d => d.Id)

                .AsSplitQuery()
                .SingleOrDefault(d =>
                    d.Id == deckId && d.OwnerId == userId && !d.TradesTo.Any()));



    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
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

            if  (deck.Wants.Any() || deck.GiveBacks.Any())
            {
                PostMessage = "Successfully exchanged requests, but not all could be fullfilled";
            }
            else
            {
                PostMessage = "Successfully exchanged all card requests";
            }
        }
        catch (DbUpdateException e)
        {
            _logger.LogError($"ran into db error {e}");

            PostMessage = "Ran into issue while trying to exchange";
        }

        return RedirectToPage("History", new { id });
    }


    private async Task ApplyChangesAsync(Deck deck, CancellationToken cancel)
    {
        // TODO: add better fix for possible overlap of returning a card 
        // with the same name as a wanted card
        // potential fix could be to transfer returning cards
        // straight to wanted cards

        var wantCards = deck.Wants.Select(w => w.CardId);
        var giveCards = deck.GiveBacks.Select(g => g.CardId);

        if (wantCards.Intersect(giveCards).Any())
        {
            return;
        }

        bool lackReturns = deck.GiveBacks
            .GroupJoin(deck.Cards,
                g => g.CardId, a => a.CardId,
                (give, amounts) => give.Copies > amounts.Sum(a => a.Copies))
            .Any(gt => gt);

        if (lackReturns)
        {
            return;
        }

        await _dbContext.ExchangeAsync(deck, cancel);

        var emptyTrades = deck.Cards
            .Where(a => a.Copies == 0)
            .Join(deck.TradesFrom,
                a => a.CardId, t => t.CardId,
                (_, trade) => trade);

        _dbContext.Trades.RemoveRange(emptyTrades);
    }
}
