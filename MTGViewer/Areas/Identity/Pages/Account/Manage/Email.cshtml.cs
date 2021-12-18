using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

namespace MTGViewer.Areas.Identity.Pages.Account.Manage;

public class EmailModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly SignInManager<CardUser> _signInManager;
    private readonly EmailVerification _emailVerify;

    public EmailModel(
        UserManager<CardUser> userManager,
        SignInManager<CardUser> signInManager,
        EmailVerification emailVerify)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailVerify = emailVerify;
    }

    
    public string Email { get; set; } = null!;

    public bool IsEmailConfirmed { get; set; }


    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public InputModel? Input { get; set; }

    
    public class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "New email")]
        public string NewEmail { get; set; } = null!;
    }


    private async Task LoadAsync(CardUser user)
    {
        var email = await _userManager.GetEmailAsync(user);
        Email = email;

        Input = new InputModel
        {
            NewEmail = email,
        };

        IsEmailConfirmed = await _userManager.IsEmailConfirmedAsync(user);
    }


    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        await LoadAsync(user);
        return Page();
    }


    public async Task<IActionResult> OnPostChangeEmailAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        if (Input is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return Page();
        }

        bool emailed = await _emailVerify.SendEmailChangeAsync(user, Input.NewEmail);
        if (!emailed)
        {
            StatusMessage = "Your email is unchanged.";
            return RedirectToPage();
        }

        StatusMessage = "Confirmation link to change email sent. Please check your email.";
        return RedirectToPage();
    }


    public async Task<IActionResult> OnPostSendVerificationEmailAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
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

        var email = _userManager.GetEmailAsync(user);

        return RedirectToPage("RegisterConfirmation", new { email });
    }
}