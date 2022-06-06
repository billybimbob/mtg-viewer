using System.Linq;
using System.Paging;
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

    public SeekList<DeckPreview> Decks { get; private set; } = SeekList<DeckPreview>.Empty;

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

        var player = await GetPlayerAsync(id, cancel);

        if (player is null)
        {
            return RedirectToPage("Index");
        }

        Decks = await GetDecksAsync(player, seek, direction, cancel);

        if (!Decks.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                seek = null as int?,
                direction = SeekDirection.Forward
            });
        }

        Player = player;

        return Page();
    }

    private async Task<PlayerPreview?> GetPlayerAsync(string playerId, CancellationToken cancel)
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

    private async Task<SeekList<DeckPreview>> GetDecksAsync(
        PlayerPreview player,
        int? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        return await _dbContext.Decks
            .Where(d => d.OwnerId == player.Id)

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id)

            .Select(d => new DeckPreview
            {
                Id = d.Id,
                Name = d.Name,
                Color = d.Color,

                HeldCopies = d.Holds.Sum(h => h.Copies),
                WantCopies = d.Wants.Sum(w => w.Copies),

                HasReturns = d.Givebacks.Any(),
            })

            .SeekBy(seek, direction)
            .OrderBy<Deck>()
            .Take(_pageSize.Current)

            .ToSeekListAsync(cancel);
    }
}
