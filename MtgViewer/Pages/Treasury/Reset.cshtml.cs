using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Areas.Identity.Services;
using MtgViewer.Data;

namespace MtgViewer.Pages.Treasury;

[Authorize]
public class ResetModel : PageModel
{
    private readonly CardDbContext _dbContext;

    private readonly OwnerManager _ownerManager;
    private readonly UserManager<CardUser> _userManager;
    private readonly SignInManager<CardUser> _signManager;

    private readonly ILogger<ResetModel> _logger;

    public ResetModel(
        CardDbContext dbContext,
        OwnerManager ownerManager,
        UserManager<CardUser> userManager,
        SignInManager<CardUser> signInManager,
        ILogger<ResetModel> logger)
    {
        _dbContext = dbContext;
        _ownerManager = ownerManager;
        _userManager = userManager;
        _signManager = signInManager;
        _logger = logger;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public bool ResetRequested { get; private set; }

    public int Remaining { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Forbid();
        }

        ResetRequested = await IsResetRequestedAsync.Invoke(_dbContext, userId, cancel);

        Remaining = await _dbContext.Owners.CountAsync(o => !o.ResetRequested, cancel);

        return Page();
    }

    private static readonly Func<CardDbContext, string, CancellationToken, Task<bool>> IsResetRequestedAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string userId, CancellationToken _) =>
            dbContext.Owners
                .Where(o => o.Id == userId)
                .Select(o => o.ResetRequested)
                .SingleOrDefault());

    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var owner = await _dbContext.Owners
            .SingleOrDefaultAsync(o => o.Id == userId, cancel);

        if (owner == default)
        {
            return NotFound();
        }

        if (owner.ResetRequested)
        {
            await CancelRequestAsync(owner, cancel);
        }
        else
        {
            await ApplyRequestAsync(owner, cancel);
        }

        return RedirectToPage("Index");
    }

    private async Task CancelRequestAsync(Owner owner, CancellationToken cancel)
    {
        if (!owner.ResetRequested)
        {
            throw new InvalidOperationException("User should have reset request active");
        }

        try
        {
            var cardUser = await _userManager.GetUserAsync(User);

            owner.ResetRequested = false;

            await _dbContext.SaveChangesAsync(cancel);

            await _signManager.RefreshSignInAsync(cardUser);

            PostMessage = "Successfully canceled reset request";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            PostMessage = "Ran into error canceling reset request";
        }
    }

    private async Task ApplyRequestAsync(Owner owner, CancellationToken cancel)
    {
        if (owner.ResetRequested)
        {
            throw new InvalidOperationException("User should not have reset requested");
        }

        bool areAllRequested = await _dbContext.Owners
            .Where(o => o.Id != owner.Id)
            .AllAsync(o => o.ResetRequested, cancel);

        try
        {
            if (areAllRequested)
            {
                await _ownerManager.ResetAsync(cancel);
                return;
            }

            var cardUser = await _userManager.GetUserAsync(User);

            var suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == owner.Id)
                .ToListAsync(cancel);

            var trades = await _dbContext.Trades
                .Where(t => t.To.OwnerId == owner.Id || t.From.OwnerId == owner.Id)
                .ToListAsync(cancel);

            owner.ResetRequested = true;

            _dbContext.Suggestions.RemoveRange(suggestions);
            _dbContext.Trades.RemoveRange(trades);

            await _dbContext.SaveChangesAsync(cancel);

            await _signManager.RefreshSignInAsync(cardUser);

            int remaining = await _dbContext.Owners.CountAsync(o => !o.ResetRequested, cancel);

            PostMessage = $"Reset request applied, but waiting on {remaining} more users to apply the reset";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            PostMessage = "Ran into issue requesting for card data resets";
        }
    }
}
