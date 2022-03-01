using System;
using System.Collections.Generic;
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

    private HashSet<int>? _sharedMap;
    private HashSet<(int, int, int?)>? _firstTransfer;

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

    public Seek Seek { get; private set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;


    public async Task OnGetAsync(
        int? seek,
        bool backtrack,
        string? tz, 
        CancellationToken cancel)
    {
        var changes = await ChangesForHistory()
            .SeekBy(c => c.Id, seek, _pageSize, backtrack)
            .ToSeekListAsync(cancel);

        _firstTransfer = changes
            .DistinctBy(c => c.TransactionId)
            .Select(c => (c.TransactionId, c.ToId, c.FromId))
            .ToHashSet();

        _sharedMap = await SharedTransactionIds(changes)
            .AsAsyncEnumerable()
            .ToHashSetAsync(cancel);

        Transfers = changes
            .GroupBy(c => (c.Transaction, c.To, c.From),
                (tft, changeGroup) =>
                    new Transfer(
                        tft.Transaction, tft.To, tft.From, changeGroup.ToList()))
            .ToList();

        Seek = changes.Seek;

        UpdateTimeZone(tz);
    }


    private IQueryable<Change> ChangesForHistory()
    {
        return _dbContext.Changes
            .Where(c => c.From is Box || c.To is Box)

            .Include(c => c.Transaction)
            .Include(c => c.To)
            .Include(c => c.From)
            .Include(c => c.Card)

            .OrderByDescending(c => c.Transaction.AppliedAt)
                .ThenByDescending(c => c.From == null)
                .ThenBy(c => c.From!.Name)
                .ThenBy(c => c.To.Name)
                    .ThenBy(c => c.Card.Name)
                    .ThenBy(c => c.Amount)
                    .ThenBy(c => c.Id)

            // no projection because lack of identity resolution

            .AsNoTrackingWithIdentityResolution();
    }


    private IQueryable<Transaction> SharedTransactions()
    {
        return _dbContext.Transactions
            .Where(t => t.Changes
                .All(c => c.To is Box && (c.From == null || c.From is Box)))
            .OrderBy(t => t.Id);
    }


    private IQueryable<int> SharedTransactionIds(IEnumerable<Change> changes)
    {
        var transactionIds = changes
            .Select(c => c.TransactionId)
            .ToHashSet();

        return SharedTransactions()
            .Select(t => t.Id)
            .Where(tid => transactionIds.Contains(tid));
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


    public bool IsShared(Transaction transaction)
    {
        return _sharedMap?.Contains(transaction.Id) ?? false;
    }


    public bool IsFirstTransfer(Transaction transaction, Location to, Location? from)
    {
        return _firstTransfer?.Contains((transaction.Id, to.Id, from?.Id)) ?? false;
    }


    public async Task<IActionResult> OnPostAsync(int transactionId, CancellationToken cancel)
    {
        if (!_signInManager.IsSignedIn(User))
        {
            return Challenge();
        }

        var authorized = await _authorization.AuthorizeAsync(User, CardPolicies.ChangeTreasury);
        if (!authorized.Succeeded)
        {
            return Forbid();
        }

        var transaction = await SharedTransactions()
            .Include(t => t.Changes) // unbounded, keep eye on
            .SingleOrDefaultAsync(t => t.Id == transactionId, cancel);

        if (transaction == default)
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