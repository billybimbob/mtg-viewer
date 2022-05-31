using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Areas.Identity.Services;

namespace MtgViewer.Areas.Identity.Pages.Account.Manage;

public class DeletePersonalDataModel : PageModel
{
    private readonly OwnerManager _ownerManager;
    private readonly UserManager<CardUser> _userManager;
    private readonly SignInManager<CardUser> _signInManager;
    private readonly ILogger<DeletePersonalDataModel> _logger;

    public DeletePersonalDataModel(
        OwnerManager referenceManager,
        UserManager<CardUser> userManager,
        SignInManager<CardUser> signInManager,
        ILogger<DeletePersonalDataModel> logger)
    {
        _ownerManager = referenceManager;
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
        public string Password { get; set; } = default!;
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

    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
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
        if (RequirePassword)
        {
            bool correctPassword = await _userManager.CheckPasswordAsync(user, Input.Password);
            if (!correctPassword)
            {
                ModelState.AddModelError(string.Empty, "Incorrect password.");
                return Page();
            }
        }

        bool resetCheckPassed = await CheckAndApplyResetAsync(user.Id, cancel);
        if (!resetCheckPassed)
        {
            return Page();
        }

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Failed to delete the user");
            return Page();
        }

        bool ownerDeleted = await _ownerManager.DeleteAsync(user, cancel);
        if (!ownerDeleted)
        {
            ModelState.AddModelError(string.Empty, "Failed to delete the user");
            return Page();
        }

        string? userId = await _userManager.GetUserIdAsync(user);

        await _signInManager.SignOutAsync();

        _logger.LogInformation("User with ID '{UserId}' deleted themselves.", userId);

        return Redirect("~/");
    }

    private async Task<bool> CheckAndApplyResetAsync(string userId, CancellationToken cancel)
    {
        bool areAllRequested = await _ownerManager.Owners
            .Where(o => o.Id != userId)
            .AllAsync(o => o.ResetRequested, cancel);

        if (!areAllRequested)
        {
            return true;
        }

        try
        {
            await _ownerManager.ResetAsync(cancel);

            return true;
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            ModelState.AddModelError(string.Empty, "Ran into issue applying delete");

            return false;
        }
    }
}
