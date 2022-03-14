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
public class RequestModel : PageModel
{
    private CardDbContext _dbContext;
    private UserManager<CardUser> _userManager;
    private readonly int _pageSize;

    private ILogger<RequestModel> _logger;

    public RequestModel(
        CardDbContext dbContext, 
        UserManager<CardUser> userManager, 
        PageSizes pageSizes,
        ILogger<RequestModel> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _pageSize = pageSizes.GetPageModelSize<RequestModel>();
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public DeckRequest Deck { get; private set; } = default!;

    public OffsetList<CardCopies> Requests { get; private set; } = OffsetList<CardCopies>.Empty;


    public async Task<IActionResult> OnGetAsync(int id, int? offset, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Challenge();
        }

        var deck = await DeckRequestAsync.Invoke(_dbContext, id, userId, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.SentTrades)
        {
            return RedirectToPage("Status", new { id });
        }

        // offset paging used since the amount of pages is unlikely 
        // to be that high

        var requests = await RequestMatches(deck)
            .PageBy(offset, _pageSize)
            .ToOffsetListAsync(cancel);

        if (!requests.Any() && offset is not null)
        {
            return RedirectToPage(new { offset = null as int? });
        }

        Deck = deck;
        Requests = requests;

        return Page();
    }


    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<DeckRequest?>> DeckRequestAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, string userId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId
                    && d.OwnerId == userId
                    && d.Wants.Any())

                .Select(d => new DeckRequest
                {
                    Id = d.Id,
                    Name = d.Name,

                    Owner = new OwnerPreview
                    {
                        Id = d.OwnerId,
                        Name = d.Owner.Name
                    },

                    SentTrades = d.TradesTo.Any()
                })
                .SingleOrDefault());


    private IQueryable<CardCopies> RequestMatches(DeckRequest deck)
    {
        var deckWants = _dbContext.Wants
            .Where(w => w.LocationId == deck.Id)
            .OrderBy(w => w.Card.Name)
                .ThenBy(w => w.Card.SetName)
                .ThenBy(w => w.Id);

        var possibleTargets = _dbContext.Users
            .Where(u => u.Id != deck.Owner.Id && !u.ResetRequested)
            .SelectMany(u => u.Decks)
            .SelectMany(d => d.Cards, (_, a) => a.Card.Name)
            .Distinct();

        var wantMatches = deckWants
            // left join
            .GroupJoin( possibleTargets,
                w => w.Card.Name, name => name,
                (Want, Names) => new { Want, Names })
            .SelectMany(
                wn => wn.Names.DefaultIfEmpty(),
                (wn, Name) => new { wn.Want, HasMatch = Name != default });

        return wantMatches
            .Where(wh => wh.HasMatch)
            .Select(wh => new CardCopies
            {
                Id = wh.Want.CardId,
                Name = wh.Want.Card.Name,
                SetName = wh.Want.Card.SetName,

                ManaCost = wh.Want.Card.ManaCost,
                Rarity = wh.Want.Card.Rarity,
                ImageUrl = wh.Want.Card.ImageUrl,

                Copies = wh.Want.Copies
            });
    }


    private IQueryable<Trade> TradeRequests(DeckRequest deck)
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
            .SelectMany(d => d.Cards)
            .OrderBy(a => a.Id)

            // intentionally leave wants unbounded by target since
            // that cap will be handled later

            .Join( wantNames,
                a => a.Card.Name,
                want => want.Name,
                (target, want) => new Trade
                {
                    ToId = deck.Id,
                    FromId = target.LocationId,
                    Card = target.Card,
                    Amount = want.Copies
                });
    }



    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Challenge();
        }

        var deck = await DeckRequestAsync.Invoke(_dbContext, id, userId, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        if (deck.SentTrades)
        {
            PostMessage = "Request is already sent";
            return RedirectToPage("Status", new { id });
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

            return RedirectToPage("Status", new { id });
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into issue while trying to send request";

            return RedirectToPage("Index");
        }
    }

}
