using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Transactions;

public class CardModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;
    private readonly ILogger<IndexModel> _logger;

    public CardModel(CardDbContext dbContext, PageSize pageSize, ILogger<IndexModel> logger)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
        _logger = logger;
    }

    [TempData]
    public string? TimeZoneId { get; set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;

    public string CardName { get; set; } = string.Empty;

    public SeekList<ChangePreview> Changes { get; private set; } = SeekList.Empty<ChangePreview>();

    public async Task<IActionResult> OnGetAsync(
        string id,
        int? seek,
        SeekDirection direction,
        string? tz,
        CancellationToken cancel)
    {
        string? cardName = await FindCardNameAsync(id, cancel);

        if (cardName is null)
        {
            return RedirectToPage("Index");
        }

        var changes = await SeekChangesAsync(id, direction, seek, cancel);

        if (!changes.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                tz,
                seek = null as int?,
                direction = SeekDirection.Forward
            });
        }

        UpdateTimeZone(tz);
        CardName = cardName;
        Changes = changes;

        return Page();
    }

    private async Task<string?> FindCardNameAsync(string id, CancellationToken cancel)
    {
        return await _dbContext.Cards
            .Where(c => c.Id == id)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(cancel);
    }

    private async Task<SeekList<ChangePreview>> SeekChangesAsync(
        string id,
        SeekDirection direction,
        int? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Changes
            .Where(c => c.CardId == id)

            .OrderByDescending(c => c.Transaction.AppliedAt)
                .ThenBy(c => c.Id)

            .SeekBy(direction)
                .After(c => c.Id == origin)
                .Take(_pageSize.Current)

            .Select(c => new ChangePreview
            {
                Copies = c.Copies,
                CardName = c.Card.Name,
                To = c.To.Name,
                From = c.From == null ? null : c.From.Name,
                Transaction = new TransactionDetails
                {
                    Id = c.Transaction.Id,
                    AppliedAt = c.Transaction.AppliedAt,
                    Copies = c.Transaction.Changes.Sum(c => c.Copies),
                    CanDelete = false
                },
            })

            .ToSeekListAsync(cancel);
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
            _logger.LogError("{Error}", e);
        }
    }
}
