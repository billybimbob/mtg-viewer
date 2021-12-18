using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

namespace MTGViewer.Areas.Identity.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly ReferenceManager _referenceManager;
    private readonly UserManager<CardUser> _userManager;
    private readonly SignInManager<CardUser> _signInManager;
    private readonly EmailVerification _emailVerify;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        ReferenceManager referenceManager,
        UserManager<CardUser> userManager,
        SignInManager<CardUser> signInManager,
        EmailVerification emailVerify,
        ILogger<RegisterModel> logger)
    {
        _referenceManager = referenceManager;
        _userManager = userManager;
        _signInManager = signInManager;
        _emailVerify = emailVerify;
        _logger = logger;
    }


    public class InputModel
    {
        [Required]
        [MaxLength(256)]
        [Display(Name = "Full Name")]
        public string Name { get; set; } = null!;

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = null!;

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = null!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = null!;
    }


    [BindProperty]
    public InputModel Input { get; set; } = null!;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }


    public void OnGet()
    { }


    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid)
        {
            // If we got this far, something failed, redisplay form
            return Page();
        }

        var user = new CardUser
        {
            Name = Input.Name,
            UserName = Input.Email,
            Email = Input.Email
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            AddResultErrors(result.Errors);
            return Page();
        }

        var userId = await _userManager.GetUserIdAsync(user);
        user.Id = userId;

        var created = await _referenceManager.CreateReferenceAsync(user);
        if (!created)
        {
            ModelState.AddModelError(string.Empty, "Issue creating user account");
            // try to delete, possibly can still fail and remain in user store
            await _userManager.DeleteAsync(user);

            return Page();
        }

        bool emailed = await _emailVerify.SendApproveRequestAsync(user);
        if (!emailed)
        {
            ModelState.AddModelError(string.Empty, "Issue creating user account");
            return Page();
        }

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