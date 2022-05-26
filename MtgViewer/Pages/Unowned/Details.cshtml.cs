using System;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Unowned;

[Authorize]
[Authorize(Policy = CardPolicies.ChangeTreasury)]
public class DetailsModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;
    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize,
        ILogger<DetailsModel> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSize;
        _logger = logger;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public UnclaimedDetails Unclaimed { get; private set; } = default!;

    public SeekList<DeckCopy> Cards { get; private set; } = SeekList<DeckCopy>.Empty;

    public async Task<IActionResult> OnGetAsync(
        int id,
        int? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        var unclaimed = await UnclaimedAsync.Invoke(_dbContext, id, cancel);

        if (unclaimed is null)
        {
            return NotFound();
        }

        Unclaimed = unclaimed;

        Cards = await UnclaimedCards(unclaimed)
            .SeekBy(seek, direction)
            .OrderBy<Card>()
            .Take(_pageSize.Current)
            .ToSeekListAsync(cancel);

        return Page();
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<UnclaimedDetails?>> UnclaimedAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int unclaimedId, CancellationToken _) =>
            dbContext.Unclaimed
                .Select(u => new UnclaimedDetails
                {
                    Id = u.Id,
                    Name = u.Name,
                    Color = u.Color,

                    HeldCopies = u.Holds.Sum(h => h.Copies),
                    WantCopies = u.Wants.Sum(w => w.Copies)
                })
                .SingleOrDefault(u => u.Id == unclaimedId));

    private IQueryable<DeckCopy> UnclaimedCards(UnclaimedDetails unclaimed)
    {
        return _dbContext.Cards
            .Where(c => c.Holds.Any(h => h.LocationId == unclaimed.Id)
                || c.Wants.Any(w => w.LocationId == unclaimed.Id))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(c => new DeckCopy
            {
                Id = c.Id,
                Name = c.Name,
                ManaCost = c.ManaCost,

                SetName = c.SetName,
                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,

                Held = c.Holds
                    .Where(h => h.LocationId == unclaimed.Id)
                    .Sum(h => h.Copies),

                Want = c.Wants
                    .Where(w => w.LocationId == unclaimed.Id)
                    .Sum(w => w.Copies)
            });
    }

    public async Task<IActionResult> OnPostClaimAsync(int id, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == userId, cancel);

        if (user == default)
        {
            return NotFound();
        }

        var unclaimed = await _dbContext.Unclaimed
            .Include(u => u.Holds)
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

        _dbContext.Decks.Attach(claimed);

        claimed.Holds.AddRange(unclaimed.Holds);
        claimed.Wants.AddRange(unclaimed.Wants);

        unclaimed.Holds.Clear();
        unclaimed.Wants.Clear();

        _dbContext.Unclaimed.Remove(unclaimed);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = $"Successfully claimed {claimed.Name}";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("Ran into issue {Error}", e);

            PostMessage = $"Ran into issue claiming {unclaimed.Name}";
        }

        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, CancellationToken cancel)
    {
        var unclaimed = await _dbContext.Unclaimed
            .Include(u => u.Holds)
                .ThenInclude(h => h.Card)
            .Include(u => u.Wants)
            .AsSplitQuery()
            .SingleOrDefaultAsync(u => u.Id == id, cancel);

        if (unclaimed == default)
        {
            return NotFound();
        }

        var cardReturns = unclaimed.Holds
            .Select(q => new CardRequest(q.Card, q.Copies));

        _dbContext.Holds.RemoveRange(unclaimed.Holds);
        _dbContext.Unclaimed.Remove(unclaimed);

        await _dbContext.AddCardsAsync(cardReturns, cancel);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = $"Successfully removed {unclaimed.Name}";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("Ran into error {Error}", e);

            PostMessage = $"Ran into issue removing {unclaimed.Name}";
        }

        return RedirectToPage("Index");
    }
}
