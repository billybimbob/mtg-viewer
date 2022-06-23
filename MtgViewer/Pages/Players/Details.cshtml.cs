using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Players;

[Authorize]
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

    public PlayerPreview Player { get; private set; } = default!;

    public SeekList<DeckPreview> Decks { get; private set; } = SeekList.Empty<DeckPreview>();

    public async Task<IActionResult> OnGetAsync(
        string id,
        int? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Challenge();
        }

        if (userId == id)
        {
            return RedirectToPage("/Decks/Index");
        }

        var player = await FindPlayerAsync(id, cancel);

        if (player is null)
        {
            return RedirectToPage("Index");
        }

        var decks = await SeekDecksAsync(player, direction, seek, cancel);

        if (!decks.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                seek = null as int?,
                direction = SeekDirection.Forward
            });
        }

        Player = player;
        Decks = decks;

        return Page();
    }

    private async Task<PlayerPreview?> FindPlayerAsync(string playerId, CancellationToken cancel)
    {
        return await _dbContext.Players
            .Where(p => p.Id == playerId)
            .Select(p => new PlayerPreview
            {
                Id = p.Id,
                Name = p.Name
            })
            .SingleOrDefaultAsync(cancel);
    }

    private async Task<SeekList<DeckPreview>> SeekDecksAsync(
        PlayerPreview player,
        SeekDirection direction,
        int? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Decks
            .Where(d => d.OwnerId == player.Id)

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id)

            .SeekBy(direction)
                .After(origin, d => d.Id)
                .ThenTake(_pageSize.Current)

            .Select(d => new DeckPreview
            {
                Id = d.Id,
                Name = d.Name,
                Color = d.Color,

                HeldCopies = d.Holds.Sum(h => h.Copies),
                WantCopies = d.Wants.Sum(w => w.Copies),

                HasReturns = d.Givebacks.Any(),
            })

            .ToSeekListAsync(cancel);
    }
}
