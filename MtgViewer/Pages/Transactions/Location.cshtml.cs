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

public class LocationModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;
    private readonly ILogger<IndexModel> _logger;

    public LocationModel(CardDbContext dbContext, PageSize pageSize, ILogger<IndexModel> logger)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
        _logger = logger;
    }

    [TempData]
    public string? TimeZoneId { get; set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;

    public string LocationName { get; set; } = string.Empty;

    public SeekList<TransactionPreview> Transactions { get; private set; } = SeekList.Empty<TransactionPreview>();

    public async Task<IActionResult> OnGetAsync(
        int id,
        int? seek,
        SeekDirection direction,
        string? tz,
        CancellationToken cancel)
    {
        string? locationName = await FindLocationNameAsync(id, cancel);

        if (locationName is null)
        {
            return RedirectToPage("Index");
        }

        var transactions = await SeekTransactionsAsync(id, direction, seek, cancel);

        if (!transactions.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                tz,
                seek = null as int?,
                direction = SeekDirection.Forward
            });
        }

        UpdateTimeZone(tz);
        LocationName = locationName;
        Transactions = transactions;

        return Page();
    }

    private async Task<string?> FindLocationNameAsync(int id, CancellationToken cancel)
    {
        return await _dbContext.Locations
            .Where(l => l.Id == id && (l is Box || l is Deck))
            .Select(l => l.Name)
            .SingleOrDefaultAsync(cancel);
    }

    private async Task<SeekList<TransactionPreview>> SeekTransactionsAsync(
        int id,
        SeekDirection direction,
        int? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Transactions
            .Where(t => t.Changes
                .Any(c => c.FromId == id || c.ToId == id))

            .OrderByDescending(t => t.AppliedAt)
                .ThenBy(t => t.Id)

            .SeekBy(direction)
                .After(t => t.Id == origin)
                .Take(_pageSize.Current)

            .Select(t => new TransactionPreview
            {
                Id = t.Id,
                AppliedAt = t.AppliedAt,

                Copies = t.Changes
                    .Sum(c => c.Copies),

                Cards = t.Changes
                    .GroupBy(
                        c => new
                        {
                            c.Card.Id,
                            c.Card.Name,
                            c.Card.SetName,
                            c.Card.ManaCost
                        },
                        (c, cs) => new LocationLink
                        {
                            Id = c.Id,
                            Name = c.Name,
                            SetName = c.SetName,
                            ManaCost = c.ManaCost,
                            Held = cs.Sum(ch => ch.Copies)
                        })

                    .OrderBy(l => l.Name)
                        .ThenBy(l => l.SetName)

                    .Take(_pageSize.Current)
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
