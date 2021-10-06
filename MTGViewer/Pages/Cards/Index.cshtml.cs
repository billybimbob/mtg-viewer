using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Services;


namespace MTGViewer.Pages.Cards
{
    public class IndexModel : PageModel
    {
        private readonly SignInManager<CardUser> _signInManager;
        private readonly int _pageSize;

        public IndexModel(SignInManager<CardUser> signInManager, PageSizes pageSizes)
        {
            _signInManager = signInManager;
            _pageSize = pageSizes.GetSize(this);
        }

        public bool IsSignedIn { get; private set; }

        public int PageSize { get; private set; }


        public void OnGet()
        {
            IsSignedIn = _signInManager.IsSignedIn(User);
            PageSize = _pageSize;
        }

    }
}
