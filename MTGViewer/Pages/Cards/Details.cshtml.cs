using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Cards
{
    public class DetailsModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly SignInManager<CardUser> _signInManager;

        public DetailsModel(SignInManager<CardUser> signInManager, CardDbContext dbContext)
        {
            _signInManager = signInManager;
            _dbContext = dbContext;
        }

        public bool IsSignedIn { get; private set; }
        public Card Card { get; private set; }


        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id is null)
            {
                return NotFound();
            }

            IsSignedIn = _signInManager.IsSignedIn(User);

            Card = await _dbContext.Cards.FindAsync(id);

            if (Card is null)
            {
                return NotFound();
            }
            else
            {
                return Page();
            }
        }
    }
}
