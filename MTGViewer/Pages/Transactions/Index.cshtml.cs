using System;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Transactions;


public class IndexModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize,
        ILogger<IndexModel> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSize;
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    [TempData]
    public string? TimeZoneId { get; set; }

    public SeekList<TransactionPreview> Transactions { get; private set; } = SeekList<TransactionPreview>.Empty;

    public string? DeckName { get; private set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;


    public async Task<IActionResult> OnGetAsync(
        int? id,
        int? seek,
        SeekDirection direction,
        string? tz,
        CancellationToken cancel)
    {
        var deckName = await GetDeckNameAsync(id, cancel);

        if (id is not null && deckName is null)
        {
            return RedirectToPage(new { id = null as int? });
        }

        var transactions = await TransactionIndices(id)
            .SeekBy(seek, direction)
            .OrderBy<Transaction>()
            .Take(_pageSize.Current)
            .ToSeekListAsync(cancel);

        if (!transactions.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                seek = null as int?,
                direction = SeekDirection.Forward
            });
        }

        UpdateTimeZone(tz);

        Transactions = transactions;
        DeckName = deckName;

        return Page();
    }


    private Task<string?> GetDeckNameAsync(int? id, CancellationToken cancel)
    {
        if (id is null)
        {
            return Task.FromResult<string?>(null);
        }

        var userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Task.FromResult<string?>(null);
        }

        return _dbContext.Decks
            .Where(d => d.Id == id && d.OwnerId == userId)
            .Select(d => d.Name)
            .SingleOrDefaultAsync(cancel);
    }


    private IQueryable<TransactionPreview> TransactionIndices(int? id)
    {
        var transactions = _dbContext.Transactions.AsQueryable();

        if (id is int)
        {
            transactions = transactions
                .Where(t => t.Changes
                    .Any(c => c.FromId == id || c.ToId == id));
        }

        return transactions
            .OrderByDescending(t => t.AppliedAt)
                .ThenBy(t => t.Id)

            .Select(t => new TransactionPreview
            {
                Id = t.Id,
                AppliedAt = t.AppliedAt,

                Copies = t.Changes
                    .Sum(c => c.Copies),

                Cards = t.Changes
                    .GroupBy(c => new
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
            _logger.LogError("{Error}", e);
        }
    }
}
