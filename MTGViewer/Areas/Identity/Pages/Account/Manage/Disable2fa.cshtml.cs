using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Pages.Account.Manage
{
    public class Disable2faModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("./Index");
    }
}
