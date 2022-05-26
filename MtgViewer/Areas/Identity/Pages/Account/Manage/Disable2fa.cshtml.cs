using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MtgViewer.Areas.Identity.Pages.Account.Manage;

public class Disable2faModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./Index");
}
