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

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Pages.Cards;


[Authorize]
[Authorize(CardPolicies.ChangeTreasury)]
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
        [Display(Name = "Number of Copies")]
        [Required(ErrorMessage = "No Copies Specified")]
        [Range(1, int.MaxValue)]
        public int RemoveCopies { get; set; }
    }


    [TempData]
    public string? PostMessage { get; set; }

    public Card Card { get; private set; } = default!;

    public string? ReturnUrl { get; private set; }

    [BindProperty]
    public InputModel? Input { get; set; }


    public async Task<IActionResult> OnGetAsync(string id, string? returnUrl, CancellationToken cancel)
    {
        if (id is null)
        {
            return NotFound();
        }

        var card = await CardForDelete(id)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancel);

        if (card == default)
        {
            return NotFound();
        }

        if (Url.IsLocalUrl(returnUrl))
        {
            ReturnUrl = returnUrl;
        }

        Card = card;

        return Page();
    }


    private IQueryable<Card> CardForDelete(string cardId)
    {
        return _dbContext.Cards
            .Where(c => c.Id == cardId)
            .Include(c => c.Amounts
                .OrderBy(a => a.NumCopies))
                .ThenInclude(a => a.Location);
    }


    public async Task<IActionResult> OnPostAsync(string id, string? returnUrl, CancellationToken cancel)
    {
        if (Input is null)
        {
            return NotFound();
        }

        var card = await CardForDelete(id).SingleOrDefaultAsync(cancel);

        if (card is null)
        {
            return NotFound();
        }

        var boxAmounts = card.Amounts.Where(a => a.Location is Box);
        int maxCopies = boxAmounts.Sum(a => a.NumCopies);

        if (!ModelState.IsValid
            || maxCopies == 0 
            || maxCopies < Input.RemoveCopies)
        {
            Card = card;
            return Page();
        }

        RemoveCopies(boxAmounts, Input.RemoveCopies);

        await _dbContext.UpdateBoxesAsync(cancel);

        _dbContext.Amounts.RemoveRange(
            card.Amounts.Where(a => a.NumCopies == 0));

        bool allRemoved = maxCopies == Input.RemoveCopies
            && !card.Amounts
                .Except(boxAmounts)
                .Any();

        if (allRemoved)
        {
            _dbContext.Cards.Remove(card);
        }

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

        if (returnUrl is not null)
        {
            return LocalRedirect(returnUrl);
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
