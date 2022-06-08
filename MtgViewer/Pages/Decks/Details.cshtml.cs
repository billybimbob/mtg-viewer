using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Decks;

public class DetailsModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public DetailsModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    public bool IsOwner { get; private set; }

    public DeckDetails Deck { get; private set; } = default!;

    public SeekList<DeckCopy> Cards { get; private set; } = SeekList<DeckCopy>.Empty;

    public async Task<IActionResult> OnGetAsync(
        int id,
        string? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        var deck = await DeckDetailsAsync.Invoke(_dbContext, id, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        var cards = await DeckCards(id)
            .SeekBy(seek, direction)
            .OrderBy<Card>()
            .Take(_pageSize.Current)
            .ToSeekListAsync(cancel);

        if (!cards.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                seek = null as int?,
                direction = SeekDirection.Forward
            });
        }

        string? userId = _userManager.GetUserId(User);

        IsOwner = deck.Owner.Id == userId;
        Deck = deck;
        Cards = cards;

        return Page();
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<DeckDetails?>> DeckDetailsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId)
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

                    HasTrades = d.TradesTo.Any()
                })
                .SingleOrDefault());

    private IQueryable<DeckCopy> DeckCards(int deckId)
    {
        return _dbContext.Cards
            .Where(c => c.Holds.Any(h => h.LocationId == deckId)
                || c.Wants.Any(w => w.LocationId == deckId)
                || c.Givebacks.Any(g => g.LocationId == deckId))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(c => new DeckCopy
            {
                Id = c.Id,
                Name = c.Name,

                SetName = c.SetName,
                ManaCost = c.ManaCost,

                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,

                Held = c.Holds
                    .Where(h => h.LocationId == deckId)
                    .Sum(h => h.Copies),

                Want = c.Wants
                    .Where(w => w.LocationId == deckId)
                    .Sum(w => w.Copies),

                Returning = c.Givebacks
                    .Where(g => g.LocationId == deckId)
                    .Sum(g => g.Copies),
            });
    }

}
