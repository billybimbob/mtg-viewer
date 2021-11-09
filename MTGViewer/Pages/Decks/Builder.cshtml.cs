using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

#nullable enable
namespace MTGViewer.Pages.Decks;


[Authorize]
public class BuilderModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;

    public BuilderModel(UserManager<CardUser> userManager, CardDbContext dbContext, PageSizes pageSizes)
    {
        _userManager = userManager;
        _dbContext = dbContext;

        PageSize = pageSizes.GetSize<BuilderModel>();
    }


    public string? UserId { get; private set; }
    public int DeckId { get; private set; }
    public int PageSize { get; }


    public async Task<IActionResult> OnGetAsync(int? id)
    {
        UserId = _userManager.GetUserId(User);

        if (id is int deckId)
        {
            var deck = await DeckForBuilder(deckId, UserId)
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (deck.TradesTo.Any())
            {
                return RedirectToPage("Viewer", new { id = deckId });
            }

            DeckId = deckId;
        }

        // the deck cannot be used as a param because of cyclic refs
        return Page();
    }


    private IQueryable<Deck> DeckForBuilder(int deckId, string userId)
    {   
        return _dbContext.Decks
            .Where(d => d.Id == deckId && d.OwnerId == userId)
            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))
            .AsNoTrackingWithIdentityResolution();
    }
}