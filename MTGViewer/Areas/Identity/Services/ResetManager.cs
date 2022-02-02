using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Areas.Identity.Services;

public class ResetManager
{
    private readonly SignInManager<CardUser> _signInManager;
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly BulkOperations _bulkOperations;

    public ResetManager(
        SignInManager<CardUser> signInManager,
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        BulkOperations bulkOperations)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _dbContext = dbContext;
        _bulkOperations = bulkOperations;
    }


    public async Task CheckAndApplyResetAsync(CancellationToken cancel = default)
    {
        bool allRequested = await _dbContext.Users.AllAsync(u => u.ResetRequested, cancel);
        if (!allRequested)
        {
            return;
        }

        var transaction = await _dbContext.Database.BeginTransactionAsync(cancel);

        await _bulkOperations.ResetAsync(cancel);

        var usersResetting = await _dbContext.Users
            .Where(u => u.ResetRequested)
            .ToListAsync(cancel);

        var resettingIds = usersResetting
            .Select(u => u.Id)
            .ToArray();

        var cardUsers = _userManager.Users
            .Where(u => resettingIds.Contains(u.Id))
            .AsAsyncEnumerable();

        await foreach (var cardUser in cardUsers)
        {
            await _userManager.AddClaimAsync(
                cardUser, new Claim(CardClaims.ChangeTreasury, cardUser.Id));
        }

        foreach (var reference in usersResetting)
        {
            reference.ResetRequested = false;
        }

        // intentionally don't pass cancel token

        await _dbContext.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}