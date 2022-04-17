using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

namespace MTGViewer.Areas.Identity.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly EmailVerification _emailVerify;

    public ForgotPasswordModel(UserManager<CardUser> userManager, EmailVerification emailVerify)
    {
        _userManager = userManager;
        _emailVerify = emailVerify;
    }

    [BindProperty]
    public InputModel? Input { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input is null || !ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
        {
            // Don't reveal that the user does not exist or is not confirmed
            return RedirectToPage("./ForgotPasswordConfirmation");
        }

        bool emailed = await _emailVerify.SendResetPasswordAsync(user);
        if (!emailed)
        {
            ModelState.AddModelError(string.Empty, "Ran into issue emailing password reset");
            return Page();
        }

        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
