using System;
using System.Collections.Generic;
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

namespace MTGViewer.Pages.Treasury;


public class HistoryModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;
    private readonly SignInManager<CardUser> _signInManager;
    private readonly IAuthorizationService _authorization;
    private readonly ILogger<HistoryModel> _logger;

    private readonly HashSet<(int, int, int?)> _firstTransfers = new();

    public HistoryModel(
        PageSizes pageSizes,
        CardDbContext dbContext,
        SignInManager<CardUser> signInManager,
        IAuthorizationService authorization,
        ILogger<HistoryModel> logger)
    {
        _pageSize = pageSizes.GetPageModelSize<HistoryModel>();
        _dbContext = dbContext;
        _signInManager = signInManager;
        _authorization = authorization;
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    [TempData]
    public string? TimeZoneId { get; set; }

    public IReadOnlyList<Transfer> Transfers { get; private set; } = Array.Empty<Transfer>();

    public Data.Offset Pages { get; private set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;


    public async Task OnGetAsync(int? pageIndex, string? tz, CancellationToken cancel)
    {
        var changes = await ChangesForHistory()
            .ToPagedListAsync(_pageSize, pageIndex, cancel);

        var firstTransfers = changes
            .Select(c => (c.TransactionId, c.ToId, c.FromId))
            .GroupBy(tft => tft.TransactionId,
                (_, tfts) => tfts.First());

        _firstTransfers.UnionWith(firstTransfers);

        Transfers = changes
            .GroupBy(c => (c.Transaction, c.To, c.From),
                (tft, changes) =>
                    new Transfer(
                        tft.Transaction, tft.To, tft.From, changes.ToList()) )
            .ToList();

        Pages = changes.Offset;

        UpdateTimeZone(tz);
    }


    private IQueryable<Change> ChangesForHistory()
    {
        return _dbContext.Changes
            .Where(c => c.From is Box || c.To is Box)

            .Include(c => c.Transaction)
            .Include(c => c.From)
            .Include(c => c.To)
            .Include(c => c.Card)

            .OrderByDescending(c => c.Transaction.AppliedAt)
                .ThenBy(c => c.From!.Name)
                .ThenBy(c => c.To!.Name)
                    .ThenBy(c => c.Card.Name)
                    .ThenBy(c => c.Amount)
                    .ThenBy(c => c.Id)
                    
            .AsNoTrackingWithIdentityResolution();
    }


    private void UpdateTimeZone(string? timeZoneId)
    {
        if (timeZoneId is null && TimeZoneId is not null)
        {
            timeZoneId = TimeZoneId;
        }

        if (timeZoneId is null)
        {
            return;
        }

        try
        {
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            TimeZoneId = timeZoneId;

            TempData.Keep(nameof(TimeZoneId));
        }
        catch (Exception e)
        {
            _logger.LogError(e.ToString());
        }
    }


    public bool IsFirstTransfer(Transfer transfer)
    {
        var transferKey = (transfer.Transaction.Id, transfer.To.Id, transfer.From?.Id);
        return _firstTransfers.Contains(transferKey);
    }

    public bool IsShared(Transaction transaction) =>
        transaction.Changes.All(IsShared);

    private bool IsShared(Change change) => 
        change.To is Box && change.From is Box or null;



    public async Task<IActionResult> OnPostAsync(int transactionId, CancellationToken cancel)
    {
        if (!_signInManager.IsSignedIn(User))
        {
            return Challenge();
        }

        var authorized = await _authorization.AuthorizeAsync(User, CardClaims.ChangeTreasury);
        if (!authorized.Succeeded)
        {
            return Forbid();
        }

        var transaction = await _dbContext.Transactions
            .Include(t => t.Changes)
                .ThenInclude(c => c.From)
            .Include(t => t.Changes) // unbounded, keep eye on
                .ThenInclude(c => c.To)
            .SingleOrDefaultAsync(t => t.Id == transactionId, cancel);

        if (transaction == default || !IsShared(transaction))
        {
            return NotFound();
        }

        _dbContext.Transactions.Remove(transaction);
        _dbContext.Changes.RemoveRange(transaction.Changes);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Successfully removed the transaction";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError($"ran into error while removing the transaction {e}");

            PostMessage = "Ran into issue while removing transaction";
        }

        return RedirectToPage();
    }
}