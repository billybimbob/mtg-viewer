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
using MTGViewer.Data.Projections;

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

    public DeleteLink Card { get; private set; } = default!;

    public string? ReturnUrl { get; private set; }

    [BindProperty]
    public InputModel? Input { get; set; }

    public async Task<IActionResult> OnGetAsync(string id, string? returnUrl, CancellationToken cancel)
    {
        var card = await DeleteLinkAsync.Invoke(_dbContext, id, cancel);

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

    private static readonly Func<CardDbContext, string, CancellationToken, Task<DeleteLink?>> DeleteLinkAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, CancellationToken _) =>
            dbContext.Cards
                .Where(c => c.Id == cardId)
                .Select(c => new DeleteLink
                {
                    Id = c.Id,
                    Name = c.Name,
                    SetName = c.SetName,
                    ManaCost = c.ManaCost,

                    HasDeckCopies = c.Holds
                        .Any(h => h.Location is Deck || h.Location is Unclaimed),

                    StorageCopies = c.Holds
                        .Where(h => h.Location is Box || h.Location is Excess)
                        .Sum(h => h.Copies)
                })
                .SingleOrDefault());

    private static readonly Func<CardDbContext, string, CancellationToken, Task<Card?>> CardDeleteAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, CancellationToken _) =>
            dbContext.Cards
                .Include(c => c.Flip)
                .Include(c => c.Holds
                    .OrderBy(h => h.Copies))
                    .ThenInclude(h => h.Location)
                .OrderBy(c => c.Id)
                .SingleOrDefault(c => c.Id == cardId));

    private static DeleteLink CardAsDeleteLink(Card card)
    {
        return new DeleteLink
        {
            Id = card.Id,
            Name = card.Name,
            SetName = card.SetName,
            ManaCost = card.ManaCost,

            HasDeckCopies = card.Holds
                .Any(h => h.Location is Deck || h.Location is Unclaimed),

            StorageCopies = card.Holds
                .Where(h => h.Location is Box || h.Location is Excess)
                .Sum(h => h.Copies)
        };
    }

    public async Task<IActionResult> OnPostAsync(string id, string? returnUrl, CancellationToken cancel)
    {
        if (Input is null)
        {
            return NotFound();
        }

        var card = await CardDeleteAsync.Invoke(_dbContext, id, cancel);

        if (card is null)
        {
            return NotFound();
        }

        var storageHolds = card.Holds
            .Where(h => h.Location is Box or Excess);

        int maxCopies = storageHolds.Sum(h => h.Copies);

        if (!ModelState.IsValid
            || maxCopies == 0
            || maxCopies < Input.RemoveCopies)
        {
            Card = CardAsDeleteLink(card);

            ModelState.AddModelError(
                $"{nameof(InputModel)}.{nameof(InputModel.RemoveCopies)}",
                "Number of copies to remove is invalid");

            return Page();
        }

        RemoveCopies(storageHolds, Input.RemoveCopies);

        await _dbContext.UpdateBoxesAsync(cancel);

        _dbContext.Holds.RemoveRange(
            card.Holds.Where(h => h.Copies == 0));

        bool allRemoved = maxCopies == Input.RemoveCopies
            && !card.Holds
                .Except(storageHolds)
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
            _logger.LogError("{Error}", e);

            PostMessage = $"Ran into issue deleting {card.Name}";
        }

        if (returnUrl is not null)
        {
            return LocalRedirect(returnUrl);
        }

        return Redirect("~/Cards/");
    }

    private static void RemoveCopies(IEnumerable<Hold> holds, int removeCopies)
    {
        using var e = holds.GetEnumerator();

        while (removeCopies > 0 && e.MoveNext())
        {
            var hold = e.Current;
            int minRemove = Math.Min(removeCopies, hold.Copies);

            hold.Copies -= minRemove;
            removeCopies -= minRemove;
        }
    }
}
