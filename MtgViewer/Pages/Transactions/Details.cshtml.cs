using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

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

namespace MtgViewer.Pages.Transactions;

public class DetailsModel : PageModel
{
    private readonly IAuthorizationService _authorization;
    private readonly UserManager<CardUser> _userManager;

    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    private readonly ILogger<DetailsModel> _logger;

    public DetailsModel(
        IAuthorizationService authorization,
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize,
        ILogger<DetailsModel> logger)
    {
        _pageSize = pageSize;
        _dbContext = dbContext;

        _userManager = userManager;
        _authorization = authorization;

        _logger = logger;
    }

    [TempData]
    public string? PostMessage { get; set; }

    [TempData]
    public string? TimeZoneId { get; set; }

    public TransactionDetails Transaction { get; private set; } = default!;

    public IReadOnlyList<Move> Moves { get; private set; } = Array.Empty<Move>();

    public Seek Seek { get; private set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;

    public DateTime AppliedAt => TimeZoneInfo.ConvertTimeFromUtc(Transaction.AppliedAt, TimeZone);

    public async Task<IActionResult> OnGetAsync(
        int id,
        int? seek,
        SeekDirection direction,
        string? tz,
        CancellationToken cancel)
    {
        var transaction = await FindTransactionAsync(id, cancel);

        if (transaction is null)
        {
            return NotFound();
        }

        var changes = await SeekChangesAsync(transaction, direction, seek, cancel);

        if (!changes.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                tz,
                seek = null as int?,
                direction = SeekDirection.Forward
            });
        }

        Transaction = transaction;

        Moves = GetMoves(changes);
        Seek = (Seek)changes.Seek;

        UpdateTimeZone(tz);

        return Page();
    }

    private async Task<TransactionDetails?> FindTransactionAsync(int id, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        var transactionQuery = userId is null
            ? TransactionDetails(id)
            : TransactionDetails(id, userId);

        return await transactionQuery.SingleOrDefaultAsync(cancel);
    }

    private IQueryable<TransactionDetails> TransactionDetails(int id)
    {
        return _dbContext.Transactions
            .Where(t => t.Id == id)
            .Select(t => new TransactionDetails
            {
                Id = t.Id,
                AppliedAt = t.AppliedAt,
                Copies = t.Changes.Sum(c => c.Copies),
                CanDelete = false
            });
    }

    private IQueryable<TransactionDetails> TransactionDetails(int id, string userId)
    {
        return _dbContext.Transactions
            .Where(t => t.Id == id)
            .Select(t => new TransactionDetails
            {
                Id = t.Id,
                AppliedAt = t.AppliedAt,
                Copies = t.Changes.Sum(c => c.Copies),

                CanDelete = t.Changes
                    .All(c =>
                        ((c.To is Box
                            || c.To is Excess
                            || c.To is Unclaimed)
                        && (c.From == null
                            || c.From is Box
                            || c.From is Excess
                            || c.From is Unclaimed))

                        || (c.To is Deck && (c.To as Deck)!.OwnerId == userId)
                        || (c.From is Deck && (c.From as Deck)!.OwnerId == userId))
            });
    }

    private async Task<SeekList<ChangeDetails>> SeekChangesAsync(
        TransactionDetails transaction,
        SeekDirection direction,
        int? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Changes
            .Where(c => c.TransactionId == transaction.Id)

            .OrderByDescending(c => c.From == null)
                .ThenBy(c => c.From!.Name)
                .ThenBy(c => c.To.Name)
                    .ThenBy(c => c.Card.Name)
                    .ThenBy(c => c.Copies)
                    .ThenBy(c => c.Id)

            .SeekBy(direction)
                .After(c => c.Id == origin)
                .ThenTake(_pageSize.Current)

            .Select(c => new ChangeDetails
            {
                Id = c.Id,

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

                Copies = c.Copies,

                Card = new CardPreview
                {
                    Id = c.CardId,
                    Name = c.Card.Name,

                    ManaCost = c.Card.ManaCost,
                    ManaValue = c.Card.ManaValue,

                    SetName = c.Card.SetName,
                    Rarity = c.Card.Rarity,
                    ImageUrl = c.Card.ImageUrl
                }
            })

            .ToSeekListAsync(cancel);
    }

    private static IReadOnlyList<Move> GetMoves(IEnumerable<ChangeDetails> changes)
        => changes
            .GroupBy(c => (c.To, c.From),
                (tf, changes) => new Move
                {
                    To = tf.To,
                    From = tf.From,
                    Changes = changes
                })
            .ToList();

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
            _logger.LogError("{Error}", e);
        }
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<Transaction?>> DeletingTransactionAsync
        = EF.CompileAsyncQuery((CardDbContext db, int id, CancellationToken _)
            => db.Transactions
                .Include(t => t.Changes)
                    .ThenInclude(c => c.To)

                .Include(t => t.Changes)
                    .ThenInclude(c => c.From)

                .SingleOrDefault(t => t.Id == id));

    private static bool IsInvalidTransaction(Transaction transaction, string userId)
    {
        return transaction.Changes
            .Any(c => (c.To is Deck toDeck && toDeck.OwnerId != userId)
                || (c.From is Deck fromDeck && fromDeck.OwnerId != userId));
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Challenge();
        }

        var authResult = await _authorization.AuthorizeAsync(User, CardPolicies.ChangeTreasury);

        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        var transaction = await DeletingTransactionAsync.Invoke(_dbContext, id, cancel);

        if (transaction is null)
        {
            return NotFound();
        }

        if (IsInvalidTransaction(transaction, userId))
        {
            return Forbid();
        }

        try
        {
            _dbContext.Transactions.Remove(transaction);
            _dbContext.Changes.RemoveRange(transaction.Changes);

            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Successfully deleted transaction";
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError("{Error}", ex);

            PostMessage = "Ran into issue trying to delete the transaction";
        }

        return RedirectToPage("Index");
    }
}
