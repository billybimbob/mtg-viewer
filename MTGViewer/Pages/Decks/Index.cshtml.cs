using System.Collections.Paging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Decks;


[Authorize]
public class IndexModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly int _pageSize;

    public IndexModel(
        UserManager<CardUser> userManager, CardDbContext dbContext, PageSizes pageSizes)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
    }


    public enum State
    {
        Theorycraft,
        Built,
        Requesting
    }


    [TempData]
    public string? PostMessage { get; set; }

    public string UserName { get; private set; } = string.Empty;

    public SeekList<Deck> Decks { get; private set; } = SeekList<Deck>.Empty();


    public async Task<IActionResult> OnGetAsync(int? seek, bool backTrack, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return NotFound();
        }

        var userName = await _dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Name)
            .SingleOrDefaultAsync(cancel);

        if (userName is null)
        {
            return NotFound();
        }

        var decks = await GetDecksAsync(userId, seek, backTrack, cancel);

        UserName = userName;
        Decks = decks;

        return Page();
    }


    private async Task<SeekList<Deck>> GetDecksAsync(
        string userId, 
        int? seek,
        bool backTrack,
        CancellationToken cancel)
    {
        var indexDecks = DecksForIndex(userId);

        if (seek is null)
        {
            return await indexDecks
                .ToSeekListAsync(SeekPosition.Start, _pageSize, cancel);
        }

        var deck = await _dbContext.Decks
            .OrderBy(d => d.Id)
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == seek, cancel);

        if (deck is null)
        {
            return await indexDecks
                .ToSeekListAsync(SeekPosition.Start, _pageSize, cancel);
        }

        return backTrack

            ? await indexDecks
                .ToSeekListAsync(d =>
                    d.Name == deck.Name && d.Id < deck.Id
                        || d.Name.CompareTo(deck.Name) < 0,
                    
                    SeekPosition.End, _pageSize, cancel)

            : await indexDecks
                .ToSeekListAsync(d =>
                    d.Name == deck.Name && d.Id > deck.Id
                        || d.Name.CompareTo(deck.Name) > 0,

                    SeekPosition.Start, _pageSize, cancel);
    }


    private IQueryable<Deck> DecksForIndex(string userId)
    {
        return _dbContext.Decks
            .Where(d => d.OwnerId == userId)

            .Include(d => d.Cards) // unbounded: keep eye on

            .Include(d => d.Wants
                .OrderBy(w => w.Id)
                .Take(1))

            .Include(d => d.GiveBacks
                .OrderBy(g => g.Id)
                .Take(1))

            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id)

            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    public State GetDeckState(Deck deck)
    {
        if (deck.TradesTo.Any())
        {
            return State.Requesting;
        }
        else if (deck.Wants.Any() || deck.GiveBacks.Any())
        {
            return State.Theorycraft;
        }
        else
        {
            return State.Built;
        }
    }
}