using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

using MtgViewer.Areas.Identity.Data;

namespace MtgViewer.Areas.Identity.Services;

public class EmailVerification
{
    public const string Approval = "approve-account";

    private readonly UserManager<CardUser> _userManager;
    private readonly IHttpContextAccessor _httpAccessor;
    private readonly LinkGenerator _linkGenerator;

    private readonly IEmailSender _emailSender;
    private readonly AuthMessageSenderOptions _authOptions;

    public EmailVerification(
        UserManager<CardUser> userManager,
        IHttpContextAccessor httpAccessor,
        LinkGenerator linkGenerator,
        IEmailSender emailSender,
        IOptions<AuthMessageSenderOptions> authOptions)
    {
        _userManager = userManager;
        _httpAccessor = httpAccessor;
        _linkGenerator = linkGenerator;
        _emailSender = emailSender;
        _authOptions = authOptions.Value;
    }

    public async Task<bool> SendApproveRequestAsync(CardUser user)
    {
        var httpContext = _httpAccessor.HttpContext;
        if (httpContext is null)
        {
            return false;
        }

        string? userId = await _userManager.GetUserIdAsync(user);
        string email = await _userManager.GetEmailAsync(user);

        string token = await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, Approval);
        string code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        string? callbackUrl = _linkGenerator.GetUriByPage(
            httpContext,
            "/Account/ApproveConfirmation",
            handler: null,
            values: new { area = "Identity", userId, code },
            scheme: "https");

        if (callbackUrl is null)
        {
            return false;
        }

        string subject = $"Approve {user.DisplayName}";

        string message = $"New request to create an account for {email}({user.DisplayName}). "
            + $"<a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>Click here</a> to approve the request.";

        await _emailSender.SendEmailAsync(_authOptions.SenderEmail, subject, message);

        return true;
    }

    public async Task<bool> SendEmailChangeAsync(CardUser user, string newEmail)
    {
        var httpContext = _httpAccessor.HttpContext;

        if (httpContext is null)
        {
            return false;
        }

        string email = await _userManager.GetEmailAsync(user);

        if (newEmail == email)
        {
            return false;
        }

        string? userId = await _userManager.GetUserIdAsync(user);

        string token = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        string code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        string? callbackUrl = _linkGenerator.GetUriByPage(
            httpContext,
            "/Account/ConfirmEmailChange",
            handler: null,
            values: new { area = "Identity", userId, email = newEmail, code },
            scheme: "https");

        if (callbackUrl is null)
        {
            return false;
        }

        const string subject = "Confirm Email Change";

        string message = $"A request to change your email to {newEmail} was made. "
            + $"<a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>Click here</a> "
            + "to approve the change, or ignore this email if the change was not made by you.";

        await _emailSender.SendEmailAsync(newEmail, subject, message);

        return true;
    }

    public async Task<bool> SendConfirmationAsync(CardUser user)
    {
        var httpContext = _httpAccessor.HttpContext;
        if (httpContext is null)
        {
            return false;
        }

        string? userId = await _userManager.GetUserIdAsync(user);
        string email = await _userManager.GetEmailAsync(user);

        string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        string code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        string? callbackUrl = _linkGenerator.GetUriByPage(
            httpContext,
            "/Account/ConfirmEmail",
            handler: null,
            values: new { area = "Identity", userId, code },
            scheme: "https");

        if (callbackUrl is null)
        {
            return false;
        }

        const string subject = "Confirm Account";

        string message = $"Your {nameof(MtgViewer)} account was approved. "
            + $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.";

        await _emailSender.SendEmailAsync(email, subject, message);

        return true;
    }

    public async Task<bool> SendResetPasswordAsync(CardUser user)
    {
        var httpContext = _httpAccessor.HttpContext;
        if (httpContext is null)
        {
            return false;
        }

        string email = await _userManager.GetEmailAsync(user);

        // For more information on how to enable account confirmation and password reset please
        // visit https://go.microsoft.com/fwlink/?LinkID=532713
        string token = await _userManager.GeneratePasswordResetTokenAsync(user);
        string code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        string? callbackUrl = _linkGenerator.GetUriByPage(
            httpContext,
            "/Account/ResetPassword",
            handler: null,
            values: new { area = "Identity", code },
            scheme: "https");

        if (callbackUrl is null)
        {
            return false;
        }

        const string subject = "Reset Password";

        string message = "A request to reset you account password was made. "
            + $"<a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>Click here</a> "
            + "to complete the password reset, or ignore this email if the reset was not made by you.";

        await _emailSender.SendEmailAsync(email, subject, message);

        return true;
    }
}
