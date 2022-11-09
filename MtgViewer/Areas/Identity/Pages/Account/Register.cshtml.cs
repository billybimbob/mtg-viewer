using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Areas.Identity.Services;

namespace MtgViewer.Areas.Identity.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly EmailVerification _emailVerify;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        UserManager<CardUser> userManager,
        EmailVerification emailVerify,
        ILogger<RegisterModel> logger)
    {
        _userManager = userManager;
        _emailVerify = emailVerify;
        _logger = logger;
    }

    public sealed class InputModel
    {
        [Required]
        [MaxLength(256)]
        [Display(Name = "Full Name")]
        public required string Name { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public required string Email { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public required string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public required string ConfirmPassword { get; set; }
    }

    [BindProperty]
    public InputModel? Input { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (!ModelState.IsValid || Input is null)
        {
            // If we got this far, something failed, redisplay form
            return Page();
        }

        var user = new CardUser
        {
            DisplayName = Input.Name,
            UserName = Input.Email,
            Email = Input.Email
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            AddResultErrors(result.Errors);
            return Page();
        }

        string? userId = await _userManager.GetUserIdAsync(user);

        user.Id = userId;

        bool emailed = await _emailVerify.SendApproveRequestAsync(user);
        if (!emailed)
        {
            ModelState.AddModelError(string.Empty, "Issue creating user account");
            return Page();
        }

        ReturnUrl ??= Url.Content("~/");

        _logger.LogInformation("User created a new account with password.");

        return RedirectToPage("RegisterConfirmation", new { user.Email, ReturnUrl });
    }

    private void AddResultErrors(IEnumerable<IdentityError> errors)
    {
        const StringComparison comparison = StringComparison.CurrentCultureIgnoreCase;

        const string input = nameof(Input) + ".";
        const string userName = nameof(CardUser.UserName);
        const string email = nameof(InputModel.Email);
        const string password = nameof(InputModel.Password);

        foreach (var error in errors)
        {
            if (error.Code.Contains(userName, comparison))
            {
                continue;
            }

            if (error.Code.Contains(email))
            {
                ModelState.AddModelError(input + email, error.Description);
            }
            else if (error.Code.Contains(password))
            {
                ModelState.AddModelError(input + password, error.Description);
            }
            else
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

        }
    }
}
