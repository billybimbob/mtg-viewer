using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Decks;

[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public class DeleteModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public DeleteModel(
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

    public IReadOnlyList<DeckLink> Cards { get; private set; } = Array.Empty<DeckLink>();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var deck = await DeckDetailsAsync.Invoke(_dbContext, id, userId, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        var cards = await DeckCardsAsync
            .Invoke(_dbContext, id, _pageSize.Current)
            .ToListAsync(cancel);

        Deck = deck;
        Cards = cards;

        return Page();
    }

    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<DeckDetails?>> DeckDetailsAsync
        = EF.CompileAsyncQuery((CardDbContext db, int deck, string owner, CancellationToken _)
            => db.Decks
                .Where(d => d.Id == deck && d.OwnerId == owner)
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
                    ReturnCopies = d.Givebacks.Sum(g => g.Copies),

                    HasTrades = d.TradesTo.Any() || d.TradesFrom.Any()
                })
                .SingleOrDefault());

    private static readonly Func<CardDbContext, int, int, IAsyncEnumerable<DeckLink>> DeckCardsAsync
        = EF.CompileAsyncQuery((CardDbContext db, int deck, int limit)
            => db.Cards
                .Where(c => c.Holds.Any(h => h.LocationId == deck)
                    || c.Wants.Any(w => w.LocationId == deck)
                    || c.Givebacks.Any(g => g.LocationId == deck))

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
                        .Sum(w => w.Copies),

                    Returning = c.Givebacks
                        .Where(g => g.LocationId == deck)
                        .Sum(g => g.Copies),
                }));

    private static readonly Func<CardDbContext, int, string, CancellationToken, Task<Deck?>> DeckForDeleteAsync
        = EF.CompileAsyncQuery((CardDbContext db, int deck, string owner, CancellationToken _)
            => db.Decks
                .Include(d => d.Holds) // unbounded: keep eye on
                    .ThenInclude(h => h.Card)

                .Include(d => d.Wants) // unbounded: keep eye on
                .Include(d => d.Givebacks) // unbounded: keep eye on

                .Include(d => d.TradesTo) // unbounded: keep eye on
                .Include(d => d.TradesFrom) // unbounded: keep eye on

                .AsSplitQuery()
                .SingleOrDefault(d => d.Id == deck && d.OwnerId == owner));

    public async Task<IActionResult> OnPostAsync(int id, string? returnUrl, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var deck = await DeckForDeleteAsync.Invoke(_dbContext, id, userId, cancel);

        if (deck == default)
        {
            return RedirectToPage("Index");
        }

        if (deck.Holds.Any())
        {
            _dbContext.Holds.RemoveRange(deck.Holds);

            var returningCards = deck.Holds
                .Select(h => new CardRequest(h.Card, h.Copies));

            // just add since deck is being deleted
            await _dbContext.AddCardsAsync(returningCards, cancel);
        }

        _dbContext.Decks.Remove(deck);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = $"Successfully deleted {deck.Name}";
        }
        catch (DbUpdateException)
        {
            PostMessage = $"Ran into issue while trying to delete {deck.Name}";
        }

        if (returnUrl is not null)
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToPage("Index");
    }
}
