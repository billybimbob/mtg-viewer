using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Unowned;


[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public class IndexModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;

    private readonly SignInManager<CardUser> _signInManager;
    private readonly UserManager<CardUser> _userManager;

    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        CardDbContext dbContext, 
        PageSizes pageSizes,
        SignInManager<CardUser> signInManager,
        UserManager<CardUser> userManager,
        ILogger<IndexModel> logger)
    {
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
        _dbContext = dbContext;

        _signInManager = signInManager;
        _userManager = userManager;

        _logger = logger;
    }


    public SeekList<UnclaimedDetails> Unclaimed { get; private set; } = SeekList<UnclaimedDetails>.Empty;


    public async Task<IActionResult> OnGetAsync(
        int? id,
        int? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        Unclaimed = await UnclaimedDecks()
            .SeekBy(seek, direction)
            .OrderBy<Unclaimed>()
            .Take(_pageSize)
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
    {
        return RedirectToPagePreserveMethod("Details", "Claim", new { id });
    }


    public IActionResult OnPostRemove(int id)
    {
        return RedirectToPagePreserveMethod("Details", "Remove", new { id });
    }

}
