using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;

namespace MtgViewer.Areas.Identity.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<CardUser> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(SignInManager<CardUser> signInManager, ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    public IActionResult OnGet() => RedirectToPage("/Account/Manage/Index");

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        await _signInManager.SignOutAsync();

        _logger.LogInformation("User logged out.");

        if (returnUrl != null)
        {
            return LocalRedirect(returnUrl);
        }
        else
        {
            // This needs to be a redirect so that the browser performs a new
            // request and the identity for the user gets updated.
            return RedirectToPage();
        }
    }
}
