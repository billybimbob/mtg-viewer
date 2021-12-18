using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

namespace MTGViewer.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        private readonly ReferenceManager _referenceManager;
        private readonly UserManager<CardUser> _userManager;
        private readonly SignInManager<CardUser> _signInManager;

        public IndexModel(
            ReferenceManager referenceManager,
            UserManager<CardUser> userManager,
            SignInManager<CardUser> signInManager)
        {
            _referenceManager = referenceManager;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [TempData]
        public string? StatusMessage { get; set; }

        [BindProperty]
        public string? Username { get; set; }


        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            Username = user.Name;

            return Page();
        }


        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (Username is null)
            {
                return NotFound();
            }

            user.Name = Username;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Ran into issue updating name");
                return Page();
            }

            bool refUpdated = await _referenceManager.UpdateReferenceAsync(user);
            if (!refUpdated)
            {
                ModelState.AddModelError(string.Empty, "Ran into issue updating name");
                return Page();
            }

            StatusMessage = "Successfully changed user name";

            return RedirectToPage();
        }
    }
}
