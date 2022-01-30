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

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Cards;


[Authorize]
public class DeleteModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<DeleteModel> _logger;

    public DeleteModel(CardDbContext dbContext, ILogger<DeleteModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }


    public sealed class InputModel
    {
        public string CardId { get; set; } = null!;

        [Display(Name = "Number of Copies")]
        [Required(ErrorMessage = "No Copies Specified")]
        [Range(1, int.MaxValue)]
        public int RemoveCopies { get; set; }
    }


    [TempData]
    public string? PostMessage { get; set; }

    public Card Card { get; private set; } = null!;

    [BindProperty]
    public InputModel? Input { get; set; }


    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancel)
    {
        if (id is null)
        {
            return NotFound();
        }

        var card = await _dbContext.Cards
            .Include(c => c.Amounts)
                .ThenInclude(c => c.Location)
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id, cancel);

        if (card == default)
        {
            return NotFound();
        }

        Card = card;

        return Page();
    }


    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (Input is null)
        {
            return NotFound();
        }

        var card = await _dbContext.Cards
            .Include(c => c.Amounts
                .OrderBy(a => a.NumCopies))
                .ThenInclude(a => a.Location)
            .SingleOrDefaultAsync(c => c.Id == Input.CardId, cancel);

        if (card is null)
        {
            return NotFound();
        }

        var removeTargets = card.Amounts
            .Where(a => a.Location is Box || a.Location is Unclaimed);

        int maxCopies = removeTargets
            .Sum(a => a.NumCopies);

        if (!ModelState.IsValid
            || maxCopies == 0 
            || maxCopies < Input.RemoveCopies)
        {
            Card = card;
            return Page();
        }

        RemoveCopies(removeTargets, Input.RemoveCopies);

        bool allRemoved = maxCopies == Input.RemoveCopies
            && !card.Amounts
                .Except(removeTargets)
                .Any();

        if (allRemoved)
        {
            _dbContext.Cards.Remove(card);
        }

        await _dbContext.UpdateBoxesAsync(cancel);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = $"Successfully deleted {card.Name} copies";
        }
        catch (DbUpdateException e)
        {
            _logger.LogError(e.ToString());

            PostMessage = $"Ran into issue deleting {card.Name}";
        }

        return Redirect("~/Cards/");
    }


    private static void RemoveCopies(IEnumerable<Amount> amounts, int removeCopies)
    {
        using var e = amounts.GetEnumerator();

        while (removeCopies > 0 && e.MoveNext())
        {
            var amount = e.Current;
            int minRemove = Math.Min(removeCopies, amount.NumCopies);

            amount.NumCopies -= minRemove;
            removeCopies -= minRemove;
        }
    }
}
