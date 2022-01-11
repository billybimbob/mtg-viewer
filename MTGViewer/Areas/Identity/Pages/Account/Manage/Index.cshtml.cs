using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

namespace MTGViewer.Areas.Identity.Pages.Account.Manage;

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
    [Display(Name = "User Name")]
    public string? UserName { get; set; }


    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        UserName = user.DisplayName;

        return Page();
    }


    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            ModelState.AddModelError(nameof(UserName), "Invalid user name");
            return Page();
        }

        if (user.DisplayName == UserName)
        {
            StatusMessage = "Successfully changed user name";
            return RedirectToPage();
        }

        user.DisplayName = UserName;

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

        await _signInManager.RefreshSignInAsync(user);
        StatusMessage = "Successfully changed user name";

        return RedirectToPage();
    }
}
