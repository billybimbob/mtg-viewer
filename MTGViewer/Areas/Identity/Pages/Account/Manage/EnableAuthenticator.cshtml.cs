using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MTGViewer.Areas.Identity.Pages.Account.Manage;

public class EnableAuthenticatorModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./Index");
}
