using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MtgViewer.Areas.Identity.Pages.Account;

public class LoginWith2faModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./Login");
}
