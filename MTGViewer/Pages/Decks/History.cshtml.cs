using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Decks;


[Authorize]
public class HistoryModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;
    private readonly ILogger<HistoryModel> _logger;

    private readonly HashSet<(int, int, int?)> _firstTransfers = new();

    public HistoryModel(
        PageSizes pageSizes,
        CardDbContext dbContext, 
        UserManager<CardUser> userManager,
        ILogger<HistoryModel> logger)
    {
        _pageSize = pageSizes.GetPageModelSize<HistoryModel>();
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }


    public Deck Deck { get; private set; } = null!;

    public IReadOnlyList<Transfer> Transfers { get; private set; } =
        Array.Empty<Transfer>();

    public Data.Pages Pages { get; private set; }


    public async Task<IActionResult> OnGetAsync(
        int id, int? pageIndex, CancellationToken cancel)
    {
        var deck = await DeckForHistory(id).SingleOrDefaultAsync(cancel);

        if (deck == default)
        {
            return NotFound();
        }

        var changes = await ChangesForHistory(id)
            .ToPagedListAsync(_pageSize, pageIndex, cancel);

        var firstTransfers = changes
            .Select(c => (c.TransactionId, c.ToId, c.FromId))
            .GroupBy(tft => tft.TransactionId,
                (_, tfts) => tfts.First());

        _firstTransfers.UnionWith(firstTransfers);

        Deck = deck;

        Transfers = changes
            .GroupBy(c => (c.Transaction, c.To, c.From),
                (tft, changeGroup) =>
                    new Transfer(
                        tft.Transaction, tft.To, tft.From, changeGroup.ToList()))
            .ToList();

        Pages = changes.Pages;

        return Page();
    }


    private IQueryable<Deck> DeckForHistory(int deckId)
    {
        var userId = _userManager.GetUserId(User);

        return _dbContext.Decks
            .Where(d => d.Id == deckId && d.OwnerId == userId)
            .AsNoTracking();
    }


    private IQueryable<Change> ChangesForHistory(int deckId)
    {
        return _dbContext.Changes
            .Where(c => c.ToId == deckId || c.FromId == deckId)
            
            .Include(c => c.Transaction)
            .Include(c => c.From)
            .Include(c => c.To)
            .Include(c => c.Card)

            .OrderByDescending(c => c.Transaction.AppliedAt)
                .ThenBy(c => c.From!.Name)
                .ThenBy(c => c.To!.Name)
                    .ThenBy(c => c.Card.Name)
                    .ThenBy(c => c.Amount)
                    
            .AsNoTrackingWithIdentityResolution();
    }


    public bool IsFirstTransfer(Transfer transfer)
    {
        var transferKey = (transfer.Transaction.Id, transfer.To.Id, transfer.From?.Id);
        return _firstTransfers.Contains(transferKey);
    }



    public async Task<IActionResult> OnPostAsync(int transactionId, CancellationToken cancel)
    {
        var transaction = await _dbContext.Transactions
            .Include(t => t.Changes)
                .ThenInclude(c => c.From)
            .Include(t => t.Changes) // unbounded: keep eye on
                .ThenInclude(c => c.To)
            .SingleOrDefaultAsync(t => t.Id == transactionId, cancel);

        if (transaction == default || IsNotUserTransaction(transaction))
        {
            return NotFound();
        }

        _dbContext.Transactions.Remove(transaction);
        _dbContext.Changes.RemoveRange(transaction.Changes);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Successfully removed deck transaction log";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError($"issue removing changes {e}");
        }

        return RedirectToPage();
    }


    private bool IsNotUserTransaction(Transaction transaction)
    {
        var userId = _userManager.GetUserId(User);

        bool IsInvalidLocation(Location? loc)
        {
            return loc is Deck deck && deck.OwnerId != userId;
        }

        return transaction.Changes.Any(c => 
            IsInvalidLocation(c.To) || IsInvalidLocation(c.From));
    }


    // public async Task<IActionResult> OnPostRemoveAsync(int transactionId)


    // public async Task<IActionResult> OnPostUndoAsync(int transactionId)
    // {
    // }
}