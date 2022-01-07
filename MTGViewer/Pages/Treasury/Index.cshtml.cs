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
    private readonly ITreasuryQuery _treasury;

    public IndexModel(
        PageSizes pageSizes, 
        SignInManager<CardUser> signInManager, 
        ITreasuryQuery treasury)
    {
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
        _signInManager = signInManager;
        _treasury = treasury;
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

        bool exists = await _treasury.Boxes
            .AnyAsync(b => b.Id == boxId, cancel);

        if (!exists)
        {
            return null;
        }

        int position = await _treasury.Boxes
            .Where(b => b.Id < boxId)
            .CountAsync(cancel);

        return position / _pageSize;
    }


    private IQueryable<Box> BoxesForViewing()
    {
        return _treasury.Boxes
            .Include(b => b.Bin)

            .Include(b => b.Cards // unbounded: keep eye on
                .Where(ca => ca.NumCopies > 0)
                .OrderBy(ca => ca.Card.Name))
                .ThenInclude(ca => ca.Card)

            .OrderBy(b => b.Id)
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    // public async Task<IActionResult> OnPostOptimizeAsync()
    // {
    //     if (!IsSignedIn)
    //     {
    //         return NotFound();
    //     }

    //     try
    //     {
    //         var transaction = await _treasury.OptimizeAsync();

    //         if (transaction is null)
    //         {
    //             PostMessage = "No optimizations could be made";
    //         }
    //         else
    //         {
    //             PostMessage = "Successfully applied optimizations to storage";
    //         }
    //     }
    //     catch (DbUpdateException)
    //     {
    //         PostMessage = "Ran into issue while trying to optimize the storage";
    //     }

    //     return RedirectToPage();
    // }
}