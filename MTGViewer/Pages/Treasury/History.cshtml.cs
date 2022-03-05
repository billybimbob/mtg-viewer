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

    private IReadOnlyCollection<(int, int, int?)>? _firstTransfer;
    private IReadOnlyDictionary<int, int>? _transactionCount;

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

    public IReadOnlyList<TransferPreview> Transfers { get; private set; } = Array.Empty<TransferPreview>();

    public Seek Seek { get; private set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;


    public async Task OnGetAsync(
        int? seek,
        bool backtrack,
        string? tz, 
        CancellationToken cancel)
    {
        var changes = await ChangesForHistory()
            .SeekBy(_pageSize, backtrack)
            .WithOrigin<Change>(seek)
            .ToSeekListAsync(cancel);

        _transactionCount = changes
            .GroupBy(c => c.Transaction.Id)
            .ToDictionary(g => g.Key, g => g.Count());

        Transfers = changes
            .GroupBy(c => (c.Transaction, c.To, c.From),
                (tft, cs) => new TransferPreview(
                    tft.Transaction, tft.To, tft.From, cs.ToList()))
            .ToList();

        _firstTransfer = Transfers
            .GroupBy(t => t.Transaction.Id, (_, ts) => ts.First())
            .Select(tr => (tr.Transaction.Id, tr.To.Id, tr.From?.Id))
            .ToHashSet();

        Seek = (Seek)changes.Seek;

        UpdateTimeZone(tz);
    }


    private IQueryable<ChangePreview> ChangesForHistory()
    {
        return _dbContext.Changes
            .Where(c => c.From is Box
                || c.From is Excess
                || c.To is Box
                || c.To is Excess)

            .OrderByDescending(c => c.Transaction.AppliedAt)
                .ThenByDescending(c => c.From == null)
                .ThenBy(c => c.From!.Name)
                .ThenBy(c => c.To.Name)
                    .ThenBy(c => c.Card.Name)
                    .ThenBy(c => c.Amount)
                    .ThenBy(c => c.Id)

            .Select(c => new ChangePreview
            {
                Id = c.Id,
                Amount = c.Amount,

                Transaction = new TransactionPreview
                {
                    Id = c.TransactionId,
                    AppliedAt = c.Transaction.AppliedAt,

                    IsShared = c.Transaction.Changes
                        .All(ch => (ch.To is Box || ch.To is Excess)
                            && (ch.From == null || ch.From is Box || ch.From is Excess))
                },

                To = new LocationPreview
                {
                    Id = c.ToId,
                    Name = c.To.Name,
                    Type = c.To.Type
                },

                From = c.From == null
                    ? null
                    : new LocationPreview
                    {
                        Id = c.From.Id,
                        Name = c.From.Name,
                        Type = c.From.Type
                    },

                Card = new CardPreview
                {
                    Id = c.CardId,
                    Name = c.Card.Name,
                    ManaCost = c.Card.ManaCost,

                    SetName = c.Card.SetName,
                    Rarity = c.Card.Rarity,
                    ImageUrl = c.Card.ImageUrl
                }
            });
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


    public int GetTransactionCount(TransactionPreview transaction)
    {
        return _transactionCount?.GetValueOrDefault(transaction.Id) ?? 0;
    }


    public bool IsFirstTransfer(
        TransactionPreview transaction, 
        LocationPreview to,
        LocationPreview? from)
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

        var transaction = await _dbContext.Transactions
            .Where(t => t.Changes
                .All(c => (c.To is Box || c.To is Excess)
                    && (c.From == null || c.From is Box || c.From is Excess)))

            .Include(t => t.Changes) // unbounded, keep eye on
            .OrderBy(t => t.Id)
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