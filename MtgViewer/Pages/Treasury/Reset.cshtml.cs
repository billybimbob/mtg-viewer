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

    private readonly PlayerManager _playerManager;
    private readonly UserManager<CardUser> _userManager;
    private readonly SignInManager<CardUser> _signManager;

    private readonly ILogger<ResetModel> _logger;

    public ResetModel(
        CardDbContext dbContext,
        PlayerManager playerManager,
        UserManager<CardUser> userManager,
        SignInManager<CardUser> signInManager,
        ILogger<ResetModel> logger)
    {
        _dbContext = dbContext;
        _playerManager = playerManager;
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

        Remaining = await _dbContext.Players.CountAsync(p => !p.ResetRequested, cancel);

        return Page();
    }

    private static readonly Func<CardDbContext, string, CancellationToken, Task<bool>> IsResetRequestedAsync
        = EF.CompileAsyncQuery((CardDbContext db, string id, CancellationToken _)
            => db.Players
                .Where(p => p.Id == id)
                .Select(p => p.ResetRequested)
                .SingleOrDefault());

    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var player = await _dbContext.Players
            .SingleOrDefaultAsync(p => p.Id == userId, cancel);

        if (player == default)
        {
            return NotFound();
        }

        if (player.ResetRequested)
        {
            await CancelRequestAsync(player, cancel);
        }
        else
        {
            await ApplyRequestAsync(player, cancel);
        }

        return RedirectToPage("Index");
    }

    private async Task CancelRequestAsync(Player player, CancellationToken cancel)
    {
        if (!player.ResetRequested)
        {
            throw new InvalidOperationException("User should have reset request active");
        }

        var user = await _userManager.GetUserAsync(User);

        if (player.Id != user.Id)
        {
            throw new InvalidOperationException(
                "Player id did not match the currently signed in user");
        }

        try
        {
            player.ResetRequested = false;

            await _dbContext.SaveChangesAsync(cancel);

            await _signManager.RefreshSignInAsync(user);

            PostMessage = "Successfully canceled reset request";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            PostMessage = "Ran into error canceling reset request";
        }
    }

    private async Task ApplyRequestAsync(Player player, CancellationToken cancel)
    {
        if (player.ResetRequested)
        {
            throw new InvalidOperationException("User should not have reset requested");
        }

        var user = await _userManager.GetUserAsync(User);

        if (player.Id != user.Id)
        {
            throw new InvalidOperationException(
                "Player id did not match the currently signed in user");
        }

        try
        {
            bool areAllRequested = await _dbContext.Players
                .Where(p => p.Id != player.Id)
                .AllAsync(p => p.ResetRequested, cancel);

            if (areAllRequested)
            {
                await _playerManager.ResetAsync(cancel);
                return;
            }

            var suggestions = await _dbContext.Suggestions
                .Where(s => s.ReceiverId == player.Id)
                .ToListAsync(cancel);

            var trades = await _dbContext.Trades
                .Where(t => t.To.OwnerId == player.Id || t.From.OwnerId == player.Id)
                .ToListAsync(cancel);

            player.ResetRequested = true;

            _dbContext.Suggestions.RemoveRange(suggestions);
            _dbContext.Trades.RemoveRange(trades);

            await _dbContext.SaveChangesAsync(cancel);

            await _signManager.RefreshSignInAsync(user);

            int remaining = await _dbContext.Players.CountAsync(p => !p.ResetRequested, cancel);

            PostMessage = $"Reset request applied, but waiting on {remaining} more users to apply the reset";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            PostMessage = "Ran into issue requesting for card data resets";
        }
    }
}
