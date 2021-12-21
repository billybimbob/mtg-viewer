using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Unowned;


[Authorize]
public class IndexModel : PageModel
{
    private int _pageSize;
    private readonly CardDbContext _dbContext;
    private readonly SignInManager<CardUser> _signInManager;
    private readonly UserManager<CardUser> _userManager;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        CardDbContext dbContext, 
        PageSizes pageSizes,
        SignInManager<CardUser> signInManager,
        UserManager<CardUser> userManager,
        ILogger<IndexModel> logger)
    {
        _dbContext = dbContext;
        _pageSize = pageSizes.GetSize<IndexModel>();

        _signInManager = signInManager;
        _userManager = userManager;

        _logger = logger;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public PagedList<Unclaimed> Unclaimed { get; private set; } = PagedList<Unclaimed>.Empty;

    public IReadOnlyDictionary<int, IReadOnlyList<QuantityNameGroup>> Cards { get; private set; } =
        ImmutableDictionary<int, IReadOnlyList<QuantityNameGroup>>.Empty;


    public async Task<IActionResult> OnGetAsync(int? id, int? pageIndex, CancellationToken cancel)
    {
        if (await GetUnclaimedPageAsync(id, cancel) is int unclaimedPage)
        {
            return RedirectToPage(new { pageIndex = unclaimedPage });
        }

        Unclaimed = await UnclaimedForViewing()
            .ToPagedListAsync(_pageSize, pageIndex, cancel);

        Cards = Unclaimed
            .ToDictionary(u => u.Id, UnclaimedNameGroup);

        return Page();
    }


    private async Task<int?> GetUnclaimedPageAsync(int? id, CancellationToken cancel)
    {
        if (id is not int unclaimedId)
        {
            return null;
        }

        var name = await _dbContext.Unclaimed
            .Where(u => u.Id == unclaimedId)
            .Select(u => u.Name)
            .SingleOrDefaultAsync(cancel);

        if (name == default)
        {
            return null;
        }

        var position = await _dbContext.Unclaimed
            .Where(u => u.Name.CompareTo(name) < 0)
            .CountAsync(cancel);

        return position / _pageSize;
    }


    private IQueryable<Unclaimed> UnclaimedForViewing()
    {
        return _dbContext.Unclaimed
            .Include(u => u.Cards)
                .ThenInclude(ca => ca.Card)

            .Include(u => u.Wants)
                .ThenInclude(w => w.Card)

            .OrderBy(u => u.Name)
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    private IReadOnlyList<QuantityNameGroup> UnclaimedNameGroup(Unclaimed unclaimed)
    {
        var amountsByName = unclaimed.Cards
            .ToLookup(ca => ca.Card.Name);

        var wantsByName = unclaimed.Wants
            .ToLookup(w => w.Card.Name);

        var cardNames = amountsByName.Select(an => an.Key)
            .Union(wantsByName.Select(wn => wn.Key))
            .OrderBy(cn => cn);

        return cardNames
            .Select(cn =>
                new QuantityNameGroup( amountsByName[cn], wantsByName[cn] ))
            .ToArray();
    }

    
    public async Task<IActionResult> OnPostClaimAsync(int id, CancellationToken cancel)
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
            .SingleOrDefaultAsync(u => u.Id == userId, cancel);
        
        if (user == default)
        {
            return NotFound();
        }

        // must not be tracked so that it can be
        // replaced/updated

        var unclaimed = await _dbContext.Unclaimed
            .Include(u => u.Cards)
            .Include(u => u.Wants)
            .AsSplitQuery()
            .SingleOrDefaultAsync(u => u.Id == id, cancel);

        if (unclaimed == default)
        {
            return NotFound();
        }

        var claimed = new Deck
        {
            Name = unclaimed.Name,
            Owner = user
        };

        claimed.Cards.AddRange(unclaimed.Cards);
        claimed.Wants.AddRange(unclaimed.Wants);

        _dbContext.Decks.Update(claimed);
        _dbContext.Unclaimed.Remove(unclaimed);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Successfully claimed Deck";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError($"ran into issue {e}");

            PostMessage = "Ran into issue claiming Unclaimed Deck";
        }

        return RedirectToPage();
    }


    public async Task<IActionResult> OnPostRemoveAsync(int id, CancellationToken cancel)
    {
        if (!_signInManager.IsSignedIn(User))
        {
            return NotFound();
        }

        var unclaimed = await _dbContext.Unclaimed
            .Include(u => u.Cards)
            .Include(u => u.Wants)
            .SingleOrDefaultAsync(u => u.Id == id, cancel);

        if (unclaimed == default)
        {
            return NotFound();
        }

        _dbContext.Amounts.RemoveRange(unclaimed.Cards);
        _dbContext.Unclaimed.Remove(unclaimed);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Successfully removed Unclaimed Deck";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError($"ran into error {e}");

            PostMessage = "Ran into issue removing Unclaimed Deck";
        }

        return RedirectToPage();
    }
}