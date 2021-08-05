using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Pages.Cards
{
    public class IndexModel : PageModel
    {
        private readonly SignInManager<CardUser> _signInManager;

        public IndexModel(SignInManager<CardUser> signInManager)
        {
            _signInManager = signInManager;
        }

        public bool IsSignedIn { get; private set; }


        public void OnGet()
        {
            IsSignedIn = _signInManager.IsSignedIn(User);
        }

    }
}
