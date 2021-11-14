using System;
using System.Collections.Generic;
using System.Linq;
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
    public string PostMessage { get; set; }

    public bool TargetsExist { get; private set; }

    public Deck Deck { get; private set; }

    public IReadOnlyList<WantNameGroup> Requests { get; private set; }


    public async Task<IActionResult> OnGetAsync(int deckId)
    {
        var deck = await DeckForRequest(deckId)
            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync();

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


        TargetsExist = await TakeTargets(deck).AnyAsync();

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
                .OrderBy(ca => ca.Card.Name))
                .ThenInclude(ca => ca.Card)

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
            .Select(ca => ca.Card.Name)
            .Distinct()
            .ToArray();

        return _dbContext.Amounts
            .Where(ca => ca.Location is Deck
                && (ca.Location as Deck).OwnerId != deck.OwnerId
                && takeNames.Contains(ca.Card.Name))

            .Include(ca => ca.Card)
            .Include(ca => ca.Location);
    }



    public async Task<IActionResult> OnPostAsync(int deckId)
    {
        var deck = await DeckForRequest(deckId)
            .SingleOrDefaultAsync();

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.TradesTo.Any())
        {
            PostMessage = "Request is already sent";
            return RedirectToPage("Index");
        }

        var trades = await CreateTradesAsync(deck);

        if (!trades.Any())
        {
            PostMessage = "There are no possible decks to trade with";
            return RedirectToPage("Index");
        }

        _dbContext.Trades.AttachRange(trades);

        try
        {
            await _dbContext.SaveChangesAsync();

            PostMessage = "Request was successfully sent";
            return RedirectToPage("Status", new { deckId });
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into issue while trying to send request";
            return RedirectToPage("Index");
        }
    }


    private async Task<IEnumerable<Trade>> CreateTradesAsync(Deck deck)
    {
        if (!deck.Wants.Any())
        {
            return Enumerable.Empty<Trade>();
        }

        var requestTargets = await TakeTargets(deck).ToListAsync(); // unbounded: keep eye on

        if (!requestTargets.Any())
        {
            return Enumerable.Empty<Trade>();
        }

        return FindTradeMatches(deck, requestTargets);
    }


    private IReadOnlyList<Trade> FindTradeMatches(Deck deck, IEnumerable<Amount> targets)
    {
        // TODO: figure out how to query more on server
        // TODO: prioritize requesting from exact card matches

        var requestMatches = targets
            .GroupJoin( deck.Wants,
                target => target.Card.Name,
                want => want.Card.Name,
                (target, wantMatches) =>
                    (target, amount: wantMatches.Sum(w => w.NumCopies)));

        var newTrades = requestMatches
            .Select(ta => new Trade
            {
                Card = ta.target.Card,
                To = deck,
                From = (Deck) ta.target.Location,
                Amount = ta.amount
            });
            
        return newTrades.ToList();
    }
}