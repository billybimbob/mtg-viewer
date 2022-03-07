using System;
using System.Linq;
using System.Paging;
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


    [TempData]
    public string? PostMessage { get; set; }

    public string UserName { get; private set; } = string.Empty;

    public SeekList<DeckPreview> Decks { get; private set; } = SeekList<DeckPreview>.Empty;

    public bool HasUnclaimed { get; private set; }


    public async Task<IActionResult> OnGetAsync(
        int? seek, 
        bool backtrack,
        CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return NotFound();
        }

        var userName = await UserNameAsync.Invoke(_dbContext, userId, cancel);
        if (userName is null)
        {
            return NotFound();
        }

        var decks = await DecksForIndex(userId)
            .SeekBy(_pageSize, backtrack)
            .WithOrigin<Deck>()
            .WithKey(seek)
            .ToSeekListAsync(cancel);

        UserName = userName;
        Decks = decks;

        HasUnclaimed = await HasUnclaimedAsync.Invoke(_dbContext, cancel);
        // HasUnclaimed =  await _dbContext.Unclaimed.AnyAsync(cancel);

        return Page();
    }


    private IQueryable<DeckPreview> DecksForIndex(string userId)
    {
        return _dbContext.Decks
            .Where(d => d.OwnerId == userId)

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id)

            .Select(d => new DeckPreview
            {
                Id = d.Id,
                Name = d.Name,
                Color = d.Color,
                CardTotal = d.Cards.Sum(a => a.NumCopies),

                HasWants = d.Wants.Any(),
                HasReturns = d.GiveBacks.Any(),
                HasTradesTo = d.TradesTo.Any(),
            });
    }


    private static readonly Func<CardDbContext, string, CancellationToken, Task<string?>> UserNameAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string userId, CancellationToken _) =>
            dbContext.Users
                .Where(u => u.Id == userId)
                .Select(u => u.Name)
                .SingleOrDefault());


    private static readonly Func<CardDbContext, CancellationToken, Task<bool>> HasUnclaimedAsync
        = EF.CompileAsyncQuery(
            (CardDbContext dbContext, CancellationToken _) => dbContext.Unclaimed.Any());
}