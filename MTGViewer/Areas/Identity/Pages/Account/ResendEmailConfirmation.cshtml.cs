using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

namespace MTGViewer.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ResendEmailConfirmationModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly EmailVerification _emailVerify;

    public ResendEmailConfirmationModel(
        UserManager<CardUser> userManager, EmailVerification emailVerify)
    {
        _userManager = userManager;
        _emailVerify = emailVerify;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;
    }

    public void OnGet()
    { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Request to create account sent. An email will be sent if approved.");
            return Page();
        }

        bool emailed = user.IsApproved
            ? await _emailVerify.SendConfirmationAsync(user)
            : await _emailVerify.SendApproveRequestAsync(user);

        if (!emailed)
        {
            ModelState.AddModelError(string.Empty, "Ran into issue trying to send request");
            return Page();
        }

        return RedirectToPage("RegisterConfirmation", new { Input.Email });
    }
}
