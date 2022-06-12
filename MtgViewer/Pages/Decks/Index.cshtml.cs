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

namespace MtgViewer.Pages.Decks;

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

    [TempData]
    public string? PostMessage { get; set; }

    public string UserName { get; private set; } = string.Empty;

    public SeekList<DeckPreview> Decks { get; private set; } = SeekList<DeckPreview>.Empty;

    public bool HasUnclaimed { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        int? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Challenge();
        }

        string? userName = _userManager.GetDisplayName(User);

        if (userName is null)
        {
            return NotFound();
        }

        var decks = await DeckPreviews(userId, seek, direction)
            .ToSeekListAsync(cancel);

        if (!decks.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                seek = null as int?,
                direction = SeekDirection.Forward
            });
        }

        UserName = userName;
        Decks = decks;
        HasUnclaimed = await _dbContext.Unclaimed.AnyAsync(cancel);

        return Page();
    }

    private ISeekQueryable<DeckPreview> DeckPreviews(string userId, int? seek, SeekDirection direction)
    {
        return _dbContext.Decks
            .Where(d => d.OwnerId == userId)

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
                HasTrades = d.TradesTo.Any(),
            })

            .SeekBy(seek, direction, _pageSize.Current);
    }
}
