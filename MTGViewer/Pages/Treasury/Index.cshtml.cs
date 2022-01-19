using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;


public class IndexModel : PageModel
{
    private readonly int _pageSize;
    private readonly SignInManager<CardUser> _signInManager;
    private readonly CardDbContext _dbContext;
    private readonly TreasuryHandler _treasuryHandler;

    public IndexModel(
        PageSizes pageSizes, 
        SignInManager<CardUser> signInManager, 
        CardDbContext dbContext,
        TreasuryHandler treasuryHandler)
    {
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
        _signInManager = signInManager;
        _dbContext = dbContext;
        _treasuryHandler = treasuryHandler;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public PagedList<Box> Boxes { get; private set; } = PagedList<Box>.Empty;

    public bool IsSignedIn => _signInManager.IsSignedIn(User);


    public async Task<IActionResult> OnGetAsync(int? id, int? pageIndex, CancellationToken cancel)
    {
        if (await GetBoxPageAsync(id, cancel) is int boxPage)
        {
            return RedirectToPage(new { pageIndex = boxPage });
        }

        Boxes = await BoxesForViewing()
            .ToPagedListAsync(_pageSize, pageIndex, cancel);

        return Page();
    }


    private async Task<int?> GetBoxPageAsync(int? id, CancellationToken cancel)
    {
        if (id is not int boxId)
        {
            return null;
        }

        bool exists = await _dbContext.Boxes
            .AnyAsync(b => b.Id == boxId, cancel);

        if (!exists)
        {
            return null;
        }

        int position = await _dbContext.Boxes
            .Where(b => b.Id < boxId)
            .CountAsync(cancel);

        return position / _pageSize;
    }


    private IQueryable<Box> BoxesForViewing()
    {
        return _dbContext.Boxes
            .Include(b => b.Bin)

            .Include(b => b.Cards // unbounded: keep eye on
                .Where(ca => ca.NumCopies > 0)
                .OrderBy(ca => ca.Card.Name))
                .ThenInclude(ca => ca.Card)

            .OrderBy(b => b.Id)
            .AsNoTrackingWithIdentityResolution();
    }
}