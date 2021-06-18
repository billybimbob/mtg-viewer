using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class BuilderModel : PageModel
    {
        private UserManager<CardUser> _userManager;
        private MTGCardContext _context;

        public BuilderModel(UserManager<CardUser> userManager, MTGCardContext context)
        {
            _userManager = userManager;
            _context = context;
        }


        public CardUser CardUser { get; private set; }
        public int DeckId { get; private set; }

        public async Task OnGetAsync(int? id)
        {
            CardUser = await _userManager.GetUserAsync(User);
            DeckId = id ?? default;
            // the deck cannot be used as a param because of cyclic refs
        }

    }
}