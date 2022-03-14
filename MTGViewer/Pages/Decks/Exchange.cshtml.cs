using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
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

    public ExchangePreview Deck { get; private set; } = default!;

    public OffsetList<CardCopies> Matches { get; private set; } = OffsetList<CardCopies>.Empty;


    public async Task<IActionResult> OnGetAsync(int id, int? offset, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == default)
        {
            return NotFound();
        }

        var deck = await ExchangePreviewAsync
            .Invoke(_dbContext, id, userId, _pageSize, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        var matches = await WantTargets(deck)
            .PageBy(offset, _pageSize)
            .ToOffsetListAsync(cancel);

        if (!matches.Any())
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

                    GiveBacks = d.GiveBacks
                        .OrderBy(g => g.Card.Name)
                            .ThenBy(g => g.Card.SetName)
                            .ThenBy(g => g.Id)

                        .Take(limit)
                        .Select(g => new CardCopies
                        {
                            Id = g.CardId,
                            Name = g.Card.Name,
                            ManaCost = g.Card.ManaCost,

                            SetName = g.Card.SetName,
                            Rarity = g.Card.Rarity,
                            ImageUrl = g.Card.ImageUrl,

                            Copies = g.Copies
                        })
                })
                .SingleOrDefault());


    private IQueryable<CardCopies> WantTargets(ExchangePreview deck)
    {
        var deckWants = _dbContext.Wants
            .Where(w => w.LocationId == deck.Id)
            .Select(w => w.Card.Name)
            .Distinct();

        var totalMatches = _dbContext.Amounts
            .Where(a => a.Location is Box || a.Location is Excess)
            .Join( deckWants,
                a => a.Card.Name, name => name, (amount, _) => amount)

            .GroupBy(a => a.CardId,
                (CardId, amounts) =>
                    new { CardId, Copies = amounts.Sum(a => a.Copies) });

        return _dbContext.Cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Join( totalMatches,
                c => c.Id,
                total => total.CardId,
                (c, total) => new CardCopies
                {
                    Id = c.Id,
                    Name = c.Name,
                    SetName = c.SetName,

                    ManaCost = c.ManaCost,
                    Rarity = c.Rarity,
                    ImageUrl = c.ImageUrl,

                    Copies = total.Copies
                });
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
