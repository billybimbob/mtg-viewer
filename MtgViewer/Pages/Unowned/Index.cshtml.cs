using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Unowned;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public class IndexModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public IndexModel(CardDbContext dbContext, PageSize pageSize)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    public SeekList<UnclaimedDetails> Unclaimed { get; private set; } = SeekList<UnclaimedDetails>.Empty;

    public async Task<IActionResult> OnGetAsync(
        int? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        Unclaimed = await UnclaimedDecks()
            .SeekBy(seek, direction)
            .OrderBy<Unclaimed>()
            .Take(_pageSize.Current)
            .ToSeekListAsync(cancel);

        return Page();
    }

    private IQueryable<UnclaimedDetails> UnclaimedDecks()
    {
        return _dbContext.Unclaimed
            .OrderBy(u => u.Name)
                .ThenBy(u => u.Id)

            .Select(u => new UnclaimedDetails
            {
                Id = u.Id,
                Name = u.Name,
                Color = u.Color,

                HeldCopies = u.Holds.Sum(h => h.Copies),
                WantCopies = u.Wants.Sum(w => w.Copies)
            });
    }

    public IActionResult OnPostClaim(int id)
        => RedirectToPagePreserveMethod("Details", "Claim", new { id });

    public IActionResult OnPostRemove(int id)
        => RedirectToPagePreserveMethod("Details", "Remove", new { id });
}
