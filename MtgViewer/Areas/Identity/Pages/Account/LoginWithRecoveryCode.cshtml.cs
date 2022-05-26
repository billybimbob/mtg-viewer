using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MtgViewer.Areas.Identity.Pages.Account;

public class LoginWithRecoveryCodeModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./Login");
}
