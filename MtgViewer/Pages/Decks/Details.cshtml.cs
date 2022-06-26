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

    public SeekList<DeckCopy> Cards { get; private set; } = SeekList.Empty<DeckCopy>();

    public async Task<IActionResult> OnGetAsync(
        int id,
        string? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        var deck = await DeckDetailsAsync.Invoke(_dbContext, id, cancel);

        if (deck is null)
        {
            return NotFound();
        }

        var cards = await SeekCardsAsync(id, direction, seek, cancel);

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
        = EF.CompileAsyncQuery((CardDbContext db, int id, CancellationToken _)
            => db.Decks
                .Where(d => d.Id == id)
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

    private async Task<SeekList<DeckCopy>> SeekCardsAsync(
        int deckId,
        SeekDirection direction,
        string? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Cards
            .Where(c => c.Holds.Any(h => h.LocationId == deckId)
                || c.Wants.Any(w => w.LocationId == deckId)
                || c.Givebacks.Any(g => g.LocationId == deckId))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .SeekBy(direction)
                .After(c => c.Id == origin)
                .ThenTake(_pageSize.Current)

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
            })

            .ToSeekListAsync(cancel);
    }

}
