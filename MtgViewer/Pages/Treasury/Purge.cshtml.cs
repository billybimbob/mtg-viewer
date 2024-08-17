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

namespace MtgViewer.Pages.Treasury;

// [Authorize]
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

        [Display(Name = "Minimum Copies")]
        [Required(ErrorMessage = "No Copies Specified")]
        [Range(4, 100)]
        public int MinCopies { get; set; }
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

        string[] multiverseIds = Input.MultiverseIds.Split(Environment.NewLine, StringSplitOptions.None);

        var cards = await _dbContext.Cards
            .Where(c => multiverseIds.Contains(c.MultiverseId))
            .Include(c => c.Holds
                .OrderBy(h => h.Copies))
                .ThenInclude(h => h.Location)
            .ToListAsync(cancel);

        foreach (var card in cards)
        {
            var storageHolds = card.Holds
                .Where(h => h.Location is Box or Excess)
                .ToList();

            RetainCopies(storageHolds, Input.MinCopies);
        }

        await _dbContext.UpdateBoxesAsync(cancel);

        _dbContext.Holds.RemoveRange(
            cards.SelectMany(c => c.Holds.Where(h => h.Copies == 0)));

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

    private static void RetainCopies(IReadOnlyList<Hold> holds, int targetCopies)
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
                hold.Copies = targetCopies;
                targetCopies = 0;
            }
            else
            {
                // just delete
                hold.Copies = 0;
            }
        }
    }
}
