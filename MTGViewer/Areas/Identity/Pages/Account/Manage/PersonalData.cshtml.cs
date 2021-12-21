using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Pages.Account.Manage;

public class PersonalDataModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly ILogger<PersonalDataModel> _logger;

    public PersonalDataModel(
        UserManager<CardUser> userManager,
        ILogger<PersonalDataModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
        }

        return Page();
    }
}
