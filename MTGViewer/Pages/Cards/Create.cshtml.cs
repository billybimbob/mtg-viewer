using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;

namespace MTGViewer.Pages.Cards;

[Authorize]
public class CreateModel : PageModel
{
    public void OnGet()
    { }

    // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
    public void OnPost()
    { }

}