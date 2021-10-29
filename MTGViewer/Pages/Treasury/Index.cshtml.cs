using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;


namespace MTGViewer.Pages.Treasury
{
    public class IndexModel : PageModel
    {
        private readonly int _pageSize;
        private readonly SignInManager<CardUser> _signInManager;
        private readonly ITreasury _treasury;

        public IndexModel(
            PageSizes pageSizes, 
            SignInManager<CardUser> signInManager, 
            ITreasury treasury)
        {
            _pageSize = pageSizes.GetSize<IndexModel>();
            _signInManager = signInManager;
            _treasury = treasury;
        }


        [TempData]
        public string PostMessage { get; set; }

        public PagedList<Box> Boxes { get; private set; }

        public bool IsSignedIn => _signInManager.IsSignedIn(User);


        public async Task OnGetAsync(int? pageIndex)
        {
            Boxes = await _treasury.Boxes
                .Include(b => b.Bin)

                .Include(b => b.Cards // unbounded: keep eye on
                    .Where(ca => ca.Amount > 0)
                    .OrderBy(ca => ca.Card.Name))
                    .ThenInclude(ca => ca.Card)

                .OrderBy(b => b.Id)
                .AsNoTrackingWithIdentityResolution()
                .ToPagedListAsync(_pageSize, pageIndex);
        }


        public async Task<IActionResult> OnPostAsync()
        {
            if (!IsSignedIn)
            {
                return NotFound();
            }

            try
            {
                var transaction = await _treasury.OptimizeAsync();

                if (transaction is null)
                {
                    PostMessage = "No optimizations could be made";
                }
                else
                {
                    PostMessage = "Successfully applied optimizations to storage";
                }
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into issue while trying to optimize the storage";
            }

            return RedirectToPage("Index");
        }
    }
}