using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

namespace MTGViewer.Areas.Identity.Pages.Account;

public class ApproveConfirmationModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly EmailVerification _emailVerify;

    public ApproveConfirmationModel(
        UserManager<CardUser> userManager, EmailVerification emailVerify)
    {
        _userManager = userManager;
        _emailVerify = emailVerify;
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

        bool validToken = await _userManager.VerifyUserTokenAsync(
            user,
            TokenOptions.DefaultProvider,
            EmailVerification.Approval,
            token);

        if (!validToken)
        {
            StatusMessage = "Error approving the account.";
            return Page();
        }

        bool approved = await ApproveUserAsync(user);
        if (!approved)
        {
            StatusMessage = "Error approving the account.";
            return Page();
        }

        bool emailed = await _emailVerify.SendConfirmationAsync(user);
        if (!emailed)
        {
            StatusMessage = "Error approving the account.";
            return Page();
        }

        StatusMessage = "Account is approved, and confirmation email was sent.";
        return Page();
    }

    private async Task<bool> ApproveUserAsync(CardUser user)
    {
        if (user.IsApproved)
        {
            return true;
        }

        user.IsApproved = true;
        var updated = await _userManager.UpdateAsync(user);

        return updated.Succeeded;
    }
}
