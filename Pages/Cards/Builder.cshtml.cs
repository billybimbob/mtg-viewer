using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Cards
{
    [Authorize]
    public class BuilderModel : PageModel
    {
        private UserManager<CardUser> _userManager;

        public BuilderModel(UserManager<CardUser> userManager)
        {
            _userManager = userManager;
        }

        public Location Deck { get; private set; }


        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);

            // keep eye on, not sure if Amounts property will be passed
            Deck = new Location
            {
                Name = $"Deck #{user.Decks.Count + 1}",
                Owner = user
            };
        }

    }
}