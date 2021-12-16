using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly SignInManager<CardUser> _signInManager;

        public IndexModel(
            UserManager<CardUser> userManager,
            SignInManager<CardUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public string? Username { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }


        private async Task LoadAsync(CardUser user)
        {
            var userName = await _userManager.GetUserNameAsync(user);
            Username = userName;
        }


        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);

            return Page();
        }
    }
}
