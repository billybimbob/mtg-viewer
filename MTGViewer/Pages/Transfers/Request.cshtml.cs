using System;
using System.Collections.Generic;
using System.Linq;
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

namespace MTGViewer.Pages.Transfers;


[Authorize]
public class RequestModel : PageModel
{
    private CardDbContext _dbContext;
    private UserManager<CardUser> _userManager;
    private ILogger<RequestModel> _logger;

    public RequestModel(
        CardDbContext dbContext, UserManager<CardUser> userManager, ILogger<RequestModel> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public bool TargetsExist { get; private set; }

    public Deck Deck { get; private set; } = null!;

    public IReadOnlyList<WantNameGroup> Requests { get; private set; } = Array.Empty<WantNameGroup>();


    public async Task<IActionResult> OnGetAsync(int deckId, CancellationToken cancel)
    {
        var deck = await DeckForRequest(deckId)
            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync(cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (!deck.Wants.Any())
        {
            PostMessage = "There are no possible requests";
            return RedirectToPage("Index");
        }

        if (deck.TradesTo.Any())
        {
            return RedirectToPage("Status", new { deckId });
        }


        TargetsExist = await TakeTargets(deck).AnyAsync(cancel);

        Deck = deck;

        Requests = deck.Wants
            .GroupBy(w => w.Card.Name,
                (_, wants) => new WantNameGroup(wants))
            .ToList();

        return Page();
    }


    private IQueryable<Deck> DeckForRequest(int deckId)
    {
        var userId = _userManager.GetUserId(User);

        return _dbContext.Decks
            .Where(d => d.Id == deckId && d.OwnerId == userId)

            .Include(d => d.Cards // unbounded: keep eye on
                .OrderBy(a => a.Card.Name))
                .ThenInclude(a => a.Card)

            .Include(d => d.Wants // unbounded: keep eye on
                .OrderBy(w => w.Card.Name))
                .ThenInclude(w => w.Card)

            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))

            .AsSplitQuery();
    }


    private IQueryable<Amount> TakeTargets(Deck deck)
    {
        var takeNames = deck.Wants
            .Select(w => w.Card.Name)
            .Distinct()
            .ToArray();

        return _dbContext.Amounts
            .Where(a => a.Location is Deck
                && (a.Location as Deck)!.OwnerId != deck.OwnerId
                && a.NumCopies > 0
                && takeNames.Contains(a.Card.Name))

            .Include(a => a.Card)
            .Include(a => a.Location);
    }



    public async Task<IActionResult> OnPostAsync(int deckId, CancellationToken cancel)
    {
        var deck = await DeckForRequest(deckId).SingleOrDefaultAsync(cancel);
        if (deck == default)
        {
            return NotFound();
        }

        if (deck.TradesTo.Any())
        {
            PostMessage = "Request is already sent";
            return RedirectToPage("Index");
        }

        if (!deck.Wants.Any())
        {
            PostMessage = "There are no specified requested cards";
            return RedirectToPage("Index");
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
            return RedirectToPage("Status", new { deckId });
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into issue while trying to send request";
            return RedirectToPage("Index");
        }
    }


    private IAsyncEnumerable<Trade> TradeMatches(Deck deck)
    {
        // TODO: figure out how to query more on server
        // TODO: prioritize requesting from exact card matches

        var wants = deck.Wants.ToAsyncEnumerable();

        var requestMatches = TakeTargets(deck)
            .AsAsyncEnumerable()
            .GroupJoinAwaitWithCancellation(
                wants,
                (target, _) => ValueTask.FromResult(target.Card.Name),
                (want, _) => ValueTask.FromResult(want.Card.Name),

                async (target, wantMatches, cancel) =>
                    // intentionally leave wants unbounded by target since
                    // that cap will be handled later
                    (target, amount: await wantMatches.SumAsync(w => w.NumCopies, cancel)));

        return requestMatches
            .Select(ta => new Trade
            {
                Card = ta.target.Card,
                To = deck,
                From = (Deck) ta.target.Location,
                Amount = ta.amount
            });
    }
}