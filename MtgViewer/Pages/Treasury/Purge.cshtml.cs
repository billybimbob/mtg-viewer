using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MtgViewer.Data;
using MtgViewer.Areas.Identity.Data;

namespace MtgViewer.Pages.Treasury;

[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
public class PurgeModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<PurgeModel> _logger;

    public PurgeModel(CardDbContext dbContext, ILogger<PurgeModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public sealed class InputModel
    {
        [Display(Name = "Multiverse Ids")]
        [Required(ErrorMessage = "No Cards Specified")]
        public string MultiverseIds { get; set; } = string.Empty;

        [Display(Name = "Maximum Copies")]
        [Required(ErrorMessage = "No Copies Specified")]
        [Range(4, 100)]
        public int MaxCopies { get; set; }
    }

    [TempData]
    public string? PostMessage { get; set; }

    [BindProperty]
    public InputModel? Input { get; set; }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (Input is null)
        {
            return NotFound();
        }

        string[] multiverseIds = Input.MultiverseIds
            .Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var cards = await _dbContext.Cards
            .Where(c => multiverseIds.Contains(c.MultiverseId))
            .Include(c => c.Holds
                .OrderBy(h => h.Copies))
                .ThenInclude(h => h.Location)
            .ToListAsync(cancel);

        var blindEternity = await GetBlindEternitiesAsync(cancel);
        var transaction = GetTransaction();

        foreach (var card in cards)
        {
            var storageHolds = card.Holds
                .Where(h => h.Location is Box or Excess)
                .ToList();

            RetainCopies(blindEternity, transaction, storageHolds, Input.MaxCopies);
        }

        await _dbContext.UpdateBoxesAsync(cancel);

        _dbContext.Holds.RemoveRange(
            cards.SelectMany(c => c.Holds.Where(h => h.Copies == 0)));

        if (!transaction.Changes.Any())
        {
            _dbContext.Transactions.Remove(transaction);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Successfully deleted card copies";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError("{Error}", e);

            PostMessage = "Ran into issue clearing cards";
        }

        return RedirectToPage("Index");
    }

    private void RetainCopies(BlindEternity blindEternity, Transaction transaction, IReadOnlyList<Hold> holds, int targetCopies)
    {
        foreach (var hold in holds)
        {
            if (hold.Copies < targetCopies)
            {
                // not enough keeps
                targetCopies -= hold.Copies;
            }
            else if (hold.Copies >= targetCopies)
            {
                // too many keeps
                var change = new Change
                {
                    To = blindEternity,
                    From = hold.Location,
                    Card = hold.Card,
                    Copies = hold.Copies - targetCopies,
                    Transaction = transaction
                };

                _dbContext.Changes.Add(change);

                hold.Copies = targetCopies;
                targetCopies = 0;
            }
            else
            {
                // just delete hold
                var change = new Change
                {
                    To = blindEternity,
                    From = hold.Location,
                    Card = hold.Card,
                    Copies = hold.Copies,
                    Transaction = transaction
                };

                _dbContext.Changes.Add(change);
                hold.Copies = 0;
            }
        }
    }

    private async Task<BlindEternity> GetBlindEternitiesAsync(CancellationToken cancel)
    {
        var blindEternity = await _dbContext.BlindEternities.FirstOrDefaultAsync(cancel);

        if (blindEternity is null)
        {
            blindEternity = BlindEternity.Create();
            _dbContext.BlindEternities.Add(blindEternity);
        }

        return blindEternity;
    }

    private Transaction GetTransaction()
    {
        var transaction = new Transaction();
        _dbContext.Transactions.Add(transaction);
        return transaction;
    }
}
