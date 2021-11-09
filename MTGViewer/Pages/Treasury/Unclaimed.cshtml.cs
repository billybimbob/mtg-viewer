using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Pages.Treasury;


[Authorize]
public class UnclaimedModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly SignInManager<CardUser> _signInManager;
    private readonly UserManager<CardUser> _userManager;
    private readonly ILogger<UnclaimedModel> _logger;

    public UnclaimedModel(
        CardDbContext dbContext, 
        SignInManager<CardUser> signInManager,
        UserManager<CardUser> userManager,
        ILogger<UnclaimedModel> logger)
    {
        _dbContext = dbContext;
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }


    public IReadOnlyList<Unclaimed> Unclaimed { get; private set; }

    public async Task OnGetAsync()
    {
        Unclaimed = await _dbContext.Unclaimed
            .Include(u => u.Cards
                .OrderBy(ca => ca.Card.Name)
                    .ThenBy(ca => ca.Card.SetName))
                .ThenInclude(ca => ca.Card)
            .AsNoTrackingWithIdentityResolution()
            .ToListAsync();
    }

    
    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!_signInManager.IsSignedIn(User))
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User);

        if (userId == null)
        {
            return NotFound();
        }

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == userId);
        
        if (user == default)
        {
            return NotFound();
        }

        // must not be tracked so that it can be
        // replaced/updated

        var unclaimed = await _dbContext.Unclaimed
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == id);

        if (unclaimed == default)
        {
            return NotFound();
        }

        // should replace unclaimed since id remains the same

        var claimed = new Deck(unclaimed, user);

        _dbContext.Decks.Update(claimed);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException e)
        {
            _logger.LogError($"ran into issue {e}");
        }

        return RedirectToPage("Unclaimed");
    }
}