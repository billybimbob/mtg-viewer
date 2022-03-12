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

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;
using MTGViewer.Data;

namespace MTGViewer.Pages.Treasury;


[Authorize]
public class ResetModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly ReferenceManager _referenceManager;

    private readonly SignInManager<CardUser> _signManager;
    private readonly UserManager<CardUser> _userManager;

    private readonly ILogger<ResetModel> _logger;

    public ResetModel(
        CardDbContext dbContext,
        ReferenceManager referenceManager,
        SignInManager<CardUser> signInManager,
        UserManager<CardUser> userManager,
        ILogger<ResetModel> logger)
    {
        _dbContext = dbContext;
        _referenceManager = referenceManager;
        _signManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public bool ResetRequested { get; private set; }

    public int Remaining { get; private set; }


    public async Task OnGetAsync(CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);

        ResetRequested = await IsResetRequestedAsync.Invoke(_dbContext, userId, cancel);

        Remaining = await RemainingRequestsAsync.Invoke(_dbContext, cancel);
    }


    private static readonly Func<CardDbContext, string, CancellationToken, Task<bool>> IsResetRequestedAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string userId, CancellationToken _) =>
            dbContext.Users
                .Where(u => u.Id == userId)
                .Select(u => u.ResetRequested)
                .SingleOrDefault());


    private static readonly Func<CardDbContext, CancellationToken, Task<int>> RemainingRequestsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, CancellationToken _) =>
            dbContext.Users
                .Count(u => !u.ResetRequested));


    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == default)
        {
            return NotFound();
        }

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == userId, cancel);

        if (user == default)
        {
            return NotFound();
        }

        if (user.ResetRequested)
        {
            await CancelRequestAsync(user, cancel);
        }
        else
        {
            await ApplyRequestAsync(user, cancel);
        }

        return RedirectToPage("Index");
    }


    private async Task CancelRequestAsync(UserRef user, CancellationToken cancel)
    {
        if (!user.ResetRequested)
        {
            throw new InvalidOperationException("user should have reset request active");
        }

        try
        {
            var cardUser = await _userManager.GetUserAsync(User);

            user.ResetRequested = false;

            await _dbContext.SaveChangesAsync(cancel);

            await _signManager.RefreshSignInAsync(cardUser);

            PostMessage = "Successfully canceled reset request";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError(e.ToString());

            PostMessage = "Ran into error canceling reset request";
        }
    }


    private async Task ApplyRequestAsync(UserRef user, CancellationToken cancel)
    {
        if (user.ResetRequested)
        {
            throw new InvalidOperationException("user should not have reset requested");
        }

        bool allRequested = await _dbContext.Users
            .Where(u => u.Id != user.Id)
            .AllAsync(u => u.ResetRequested);

        try
        {
            if (allRequested)
            {
                await _referenceManager.ApplyResetAsync(cancel);
                return;
            }

            var cardUser = await _userManager.GetUserAsync(User);

            var suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == user.Id)
                .ToListAsync(cancel);

            var trades = await _dbContext.Trades
                .Where(t => t.To.OwnerId == user.Id || t.From.OwnerId == user.Id)
                .ToListAsync(cancel);

            user.ResetRequested = true;

            _dbContext.Suggestions.RemoveRange(suggestions);
            _dbContext.Trades.RemoveRange(trades);

            await _dbContext.SaveChangesAsync(cancel);

            await _signManager.RefreshSignInAsync(cardUser);

            int remaining = await RemainingRequestsAsync.Invoke(_dbContext, cancel);

            PostMessage = $"Reset request applied, but waiting on {remaining} more users to apply the reset";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError(e.ToString());

            PostMessage = "Ran into issue requesting for card data resets";
        }
    }
}