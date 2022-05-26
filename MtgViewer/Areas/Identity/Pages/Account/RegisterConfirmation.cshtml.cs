using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MtgViewer.Areas.Identity.Data;

namespace MtgViewer.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterConfirmationModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;

    public RegisterConfirmationModel(UserManager<CardUser> userManager)
    {
        _userManager = userManager;
    }

    public string Confirmation { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string? email)
    {
        if (email == null)
        {
            return RedirectToPage("/Index");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return NotFound($"Unable to load user with email '{email}'.");
        }

        Confirmation = user.IsApproved
            ? "Please check your email to confirm your account."
            : "A request to create the account was sent. "
                + $"A confirmation email will be sent to {email} if approved.";

        return Page();
    }
}
