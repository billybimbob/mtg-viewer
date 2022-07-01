using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Areas.Identity.Services;

namespace MtgViewer.Areas.Identity.Pages.Account;

public class ApproveConfirmationModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly PlayerManager _playerManager;
    private readonly EmailVerification _emailVerify;

    public ApproveConfirmationModel(
        UserManager<CardUser> userManager,
        PlayerManager playerManager,
        EmailVerification emailVerify)
    {
        _userManager = userManager;
        _playerManager = playerManager;
        _emailVerify = emailVerify;
    }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? code, CancellationToken cancel)
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

        string token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));

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

        bool created = await _playerManager.CreateAsync(user, cancel);
        if (!created)
        {
            StatusMessage = "Error approving the account.";
            return Page();
        }

        bool emailed = await _emailVerify.SendConfirmationAsync(user);
        if (!emailed)
        {
            StatusMessage = "Account was approved, but ran into an issue sending the email.";
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
