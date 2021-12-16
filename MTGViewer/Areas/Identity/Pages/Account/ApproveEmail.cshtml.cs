using System;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Pages.Account;

[Authorize]
public class ApproveEmailModel : PageModel
{
    public const string Purpose = "approve-account";

    private readonly UserManager<CardUser> _userManager;
    private readonly IEmailSender _emailSender;

    public ApproveEmailModel(
        UserManager<CardUser> userManager, IEmailSender emailSender)
    {
        _userManager = userManager;
        _emailSender = emailSender;
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

        string defaultProvider = TokenOptions.DefaultProvider;
        string approveToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));

        bool validToken = await _userManager.VerifyUserTokenAsync(user, defaultProvider, Purpose, approveToken);

        if (!validToken)
        {
            StatusMessage = "Error confirming your email.";
            return Page();
        }

        string confirmToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        string confirmCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(confirmToken));

        string? callbackUrl = Url.Page(
            "/Account/ConfirmEmail",
            pageHandler: null,
            values: new { area = "Identity", userId, code = confirmCode },
            protocol: Request.Scheme);

        if (callbackUrl is not null)
        {
            string email = await _userManager.GetEmailAsync(user);
            string message = $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.";

            await _emailSender.SendEmailAsync(email, "Confirm your email", message);
        }

        StatusMessage = "Thank you for confirming your email.";

        return Page();
    }
}