using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;


namespace MTGViewer.Pages.Boxes
{
    public class IndexModel : PageModel
    {
        private readonly int _pageSize;
        private readonly SignInManager<CardUser> _signInManager;
        private readonly ISharedStorage _sharedStorage;

        public IndexModel(
            PageSizes pageSizes, 
            SignInManager<CardUser> signInManager, 
            ISharedStorage sharedStorage)
        {
            _pageSize = pageSizes.GetSize(this);
            _signInManager = signInManager;
            _sharedStorage = sharedStorage;
        }


        [TempData]
        public string PostMessage { get; set; }

        public PagedList<Box> Boxes { get; private set; }

        public bool IsSignedIn => _signInManager.IsSignedIn(User);


        public async Task OnGetAsync(int? pageIndex)
        {
            Boxes = await _sharedStorage.Boxes
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
                var transaction = await _sharedStorage.OptimizeAsync();

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