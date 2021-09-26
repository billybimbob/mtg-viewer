using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

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

        [TempData]
        public string Reffer { get; set; }

        public bool IsSignedIn { get; private set; }
        public Card Card { get; private set; }
        public IReadOnlyList<Card> CardAlts { get; private set; }


        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id is null)
            {
                return NotFound();
            }

            IsSignedIn = _signInManager.IsSignedIn(User);

            Card = await _dbContext.Cards
                .Include(c => c.SuperTypes)
                .Include(c => c.Types)
                .Include(c => c.SubTypes)
                .AsSplitQuery()
                .SingleOrDefaultAsync(c => c.Id == id);

            if (Card == default)
            {
                return NotFound();
            }

            CardAlts = await _dbContext.Cards
                .Where(c => c.Name == Card.Name)
                .OrderBy(c => c.SetName)
                .ToListAsync();


            // kind of a hack, TODO: figure out better way than tempdata
            if (Request.Headers["Referer"] != StringValues.Empty)
            {
                var reffer = Request.Headers["Referer"].ToString();

                if (reffer.Contains("Details"))
                {
                    TempData.Keep(nameof(Reffer));
                }
                else
                {
                    Reffer = reffer;
                }
            }

            return Page();
        }
    }
}