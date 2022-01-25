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
public class ExchangeModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly TreasuryHandler _treasuryHandler;
    private readonly UserManager<CardUser> _userManager;

    private readonly CardText _cardText;
    private readonly ILogger<ExchangeModel> _logger;

    public ExchangeModel(
        CardDbContext dbContext,
        TreasuryHandler treasuryHandler,
        UserManager<CardUser> userManager,
        CardText cardText,
        ILogger<ExchangeModel> logger)
    {
        _dbContext = dbContext;
        _treasuryHandler = treasuryHandler;
        _userManager = userManager;

        _cardText = cardText;
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public Deck Deck { get; private set; } = null!;
    
    public bool HasPendings { get; private set; }


    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var deck = await DeckForExchange(id)
            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync(cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.TradesTo.Any())
        {
            return RedirectToPage("Index");
        }

        Deck = deck;

        HasPendings = deck.GiveBacks.Any() || await AnyWantsAsync(deck, cancel);

        return Page();
    }


    private IQueryable<Deck> DeckForExchange(int deckId)
    {
        var userId = _userManager.GetUserId(User);

        return _dbContext.Decks
            .Where(d => d.Id == deckId && d.OwnerId == userId)

            .Include(d => d.Cards // unbounded: keep eye one
                .OrderBy(a => a.Card.Name)
                    .ThenBy(a => a.Card.SetName))
                .ThenInclude(a => a.Card)

            .Include(d => d.Wants // unbounded: keep eye one
                .OrderBy(w => w.Card.Name)
                    .ThenBy(w => w.Card.SetName))
                .ThenInclude(w => w.Card)

            .Include(d => d.GiveBacks // unbounded: keep eye one
                .OrderBy(g => g.Card.Name)
                    .ThenBy(g => g.Card.SetName))
                .ThenInclude(g => g.Card)

            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))

            .AsSplitQuery();
    }


    private Task<bool> AnyWantsAsync(Deck deck, CancellationToken cancel)
    {
        if (!deck.Wants.Any())
        {
            return Task.FromResult(false);
        }

        var wantNames = deck.Wants
            .Select(w => w.Card.Name)
            .Distinct()
            .ToAsyncEnumerable();

        return _dbContext.Amounts
            .Where(a => a.Location is Box && a.NumCopies > 0)

            .AsAsyncEnumerable()
            .Join(wantNames,
                c => c.Card.Name, cn => cn,
                (amount, _) => amount)

            .AnyAsync(cancel)
            .AsTask();
    }



    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var deck = await DeckForExchange(id)
            .Include(d => d.TradesFrom) // unbounded, keep eye on
            .SingleOrDefaultAsync(cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.TradesTo.Any())
        {
            return RedirectToPage("Index");
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
                (give, amounts) => give.NumCopies > amounts.Sum(a => a.NumCopies))
            .Any(gt => gt);

        if (lackReturns)
        {
            return;
        }

        await _treasuryHandler.ExchangeAsync(_dbContext, deck, cancel);

        var emptyTrades = deck.Cards
            .Where(a => a.NumCopies == 0)
            .Join(deck.TradesFrom,
                a => a.CardId, t => t.CardId,
                (_, trade) => trade);

        _dbContext.Trades.RemoveRange(emptyTrades);

        deck.UpdateColors(_cardText);
    }
}
