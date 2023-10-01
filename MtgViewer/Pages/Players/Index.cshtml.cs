using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Players;

[Authorize]
public class IndexModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public IndexModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    public SeekList<PlayerPreview> Players { get; private set; } = SeekList.Empty<PlayerPreview>();

    public async Task<IActionResult> OnGetAsync(
        string? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Challenge();
        }

        Players = await SeekPlayersAsync(userId, direction, seek, cancel);

        if (!Players.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                seek = null as string,
                direction = SeekDirection.Forward
            });
        }

        return Page();
    }

    private async Task<SeekList<PlayerPreview>> SeekPlayersAsync(
        string userId,
        SeekDirection direction,
        string? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Players
            .Where(p => p.Id != userId)

            .OrderBy(p => p.Name)
                .ThenBy(p => p.Id)

            .SeekBy(direction)
                .After(p => p.Id == origin)
                .Take(_pageSize.Current)

            .Select(p => new PlayerPreview
            {
                Id = p.Id,
                Name = p.Name,
                TotalDecks = p.Decks.Count
            })

            .ToSeekListAsync(cancel);
    }
}
