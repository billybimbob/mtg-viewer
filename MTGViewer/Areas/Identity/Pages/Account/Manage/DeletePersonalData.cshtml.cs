using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

namespace MTGViewer.Areas.Identity.Pages.Account.Manage;

public class DeletePersonalDataModel : PageModel
{
    private readonly ReferenceManager _referenceManager;
    private readonly UserManager<CardUser> _userManager;
    private readonly SignInManager<CardUser> _signInManager;
    private readonly ILogger<DeletePersonalDataModel> _logger;

    public DeletePersonalDataModel(
        ReferenceManager referenceManager,
        UserManager<CardUser> userManager,
        SignInManager<CardUser> signInManager,
        ILogger<DeletePersonalDataModel> logger)
    {
        _referenceManager = referenceManager;
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }


    [BindProperty]
    public InputModel? Input { get; set; }


    public class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = null!;
    }

    public bool RequirePassword { get; set; }


    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        RequirePassword = await _userManager.HasPasswordAsync(user);
        return Page();
    }


    public async Task<IActionResult> OnPostAsync()
    {
        if (Input is null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        RequirePassword = await _userManager.HasPasswordAsync(user);
        if (RequirePassword && !await _userManager.CheckPasswordAsync(user, Input.Password))
        {
            ModelState.AddModelError(string.Empty, "Incorrect password.");
            return Page();
        }

        if (!await _referenceManager.DeleteReferenceAsync(user))
        {
            ModelState.AddModelError(string.Empty, "Failed to delete the user");
            return Page();
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Failed to delete the user");
            return Page();
        }

        var userId = await _userManager.GetUserIdAsync(user);
        await _signInManager.SignOutAsync();

        _logger.LogInformation("User with ID '{UserId}' deleted themselves.", userId);

        return Redirect("~/");
    }
}
