using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Transfers;


[Authorize]
public class IndexModel : PageModel
{
    private readonly int _pageSize;
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;

    public IndexModel(
        PageSizes pageSizes, 
        UserManager<CardUser> userManager, 
        CardDbContext dbContext)
    {
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
        _userManager = userManager;
        _dbContext = dbContext;
    }


    [TempData]
    public string? PostMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DeckIndex { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SuggestIndex { get; set; }


    public string UserName { get; private set; } = string.Empty;

    public PagedList<Deck> TradeDecks { get; private set; } = PagedList<Deck>.Empty;

    public PagedList<Suggestion> Suggestions { get; private set; } = PagedList<Suggestion>.Empty;



    public async Task<IActionResult> OnGetAsync(CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
        var user = await _dbContext.Users.FindAsync(new []{ userId }, cancel);

        if (user is null)
        {
            return NotFound();
        }

        TradeDecks = await DecksForTransfer(userId)
            .ToPagedListAsync(_pageSize, DeckIndex, cancel);

        UserName = user.Name;

        Suggestions = await SuggestionsForIndex(userId)
            .ToPagedListAsync(_pageSize, SuggestIndex, cancel);

        return Page();
    }


    public IQueryable<Deck> DecksForTransfer(string userId)
    {
        return _dbContext.Decks
            .Where(d => d.OwnerId == userId)

            .Where(d => d.TradesFrom.Any()
                || d.TradesTo.Any()
                || d.Wants.Any())

            .Include(d => d.TradesFrom
                .OrderBy(t => t.Id)
                .Take(1))

            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))

            .Include(d => d.Wants
                .OrderBy(w => w.Id)
                .Take(1))

            .OrderBy(d => d.Name)
            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    private IQueryable<Suggestion> SuggestionsForIndex(string userId)
    {
        return _dbContext.Suggestions
            .Where(s => s.ReceiverId == userId)
            .Include(s => s.Card)
            .Include(s => s.To)
            .OrderBy(s => s.SentAt)
                .ThenBy(s => s.Card.Name)
            .AsNoTrackingWithIdentityResolution();
    }


    public async Task<IActionResult> OnPostAsync(int suggestId, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);

        var suggestion = await _dbContext.Suggestions
            .SingleOrDefaultAsync(s =>
                s.Id == suggestId && s.ReceiverId == userId, cancel);

        if (suggestion is null)
        {
            PostMessage = "Specified suggestion cannot be acknowledged";
            return RedirectToPage(new { DeckIndex, SuggestIndex});
        }

        _dbContext.Suggestions.Remove(suggestion);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);
            PostMessage = "Suggestion Acknowledged";
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into issue while trying to Acknowledge";
        }

        return RedirectToPage(new { DeckIndex, SuggestIndex });
    }
}