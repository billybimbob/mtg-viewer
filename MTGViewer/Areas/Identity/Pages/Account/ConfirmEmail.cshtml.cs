using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Pages.Account;

public class ConfirmEmailModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;

    public ConfirmEmailModel(UserManager<CardUser> userManager)
    {
        _userManager = userManager;
    }

    [TempData]
    public string? StatusMessage { get; set; }


    public async Task<IActionResult> OnGetAsync(string? userId, string? code)
    {
        if (userId == null || code == null)
        {
            return RedirectToPage("/Index");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        var result = await _userManager.ConfirmEmailAsync(user, token);

        StatusMessage = result.Succeeded
            ? "Thank you for confirming your email."
            : "Error confirming your email.";

        return Page();
    }
}