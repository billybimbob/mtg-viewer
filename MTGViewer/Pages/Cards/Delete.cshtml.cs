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

    public DeleteLink Card { get; private set; } = default!;

    public string? ReturnUrl { get; private set; }

    [BindProperty]
    public InputModel? Input { get; set; }


    public async Task<IActionResult> OnGetAsync(string id, string? returnUrl, CancellationToken cancel)
    {
        if (id is null)
        {
            return NotFound();
        }

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

                    HasDeckCopies = c.Amounts
                        .Any(a => a.Location is Deck || a.Location is Unclaimed),

                    StorageCopies = c.Amounts
                        .Where(a => a.Location is Box || a.Location is Excess)
                        .Sum(a => a.Copies)
                })
                .SingleOrDefault());


    private static readonly Func<CardDbContext, string, CancellationToken, Task<Card?>> CardDeleteAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, CancellationToken _) =>
            dbContext.Cards
                .Include(c => c.Flip)
                .Include(c => c.Amounts)
                    .ThenInclude(a => a.Location)

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

            HasDeckCopies = card.Amounts
                .Any(a => a.Location is Deck || a.Location is Unclaimed),

            StorageCopies = card.Amounts
                .Where(a => a.Location is Box || a.Location is Excess)
                .Sum(a => a.Copies)
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

        var storageAmounts = card.Amounts
            .Where(a => a.Location is Box or Excess);

        int maxCopies = storageAmounts.Sum(a => a.Copies);

        if (!ModelState.IsValid
            || maxCopies == 0 
            || maxCopies < Input.RemoveCopies)
        {
            Card = CardAsDeleteLink(card);

            ModelState.AddModelError(
                $"{nameof(InputModel)}.{nameof(InputModel.RemoveCopies)}",
                "Amount of remove copies is invalid");

            return Page();
        }

        RemoveCopies(storageAmounts, Input.RemoveCopies);

        await _dbContext.UpdateBoxesAsync(cancel);

        _dbContext.Amounts.RemoveRange(
            card.Amounts.Where(a => a.Copies == 0));

        bool allRemoved = maxCopies == Input.RemoveCopies
            && !card.Amounts
                .Except(storageAmounts)
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
            int minRemove = Math.Min(removeCopies, amount.Copies);

            amount.Copies -= minRemove;
            removeCopies -= minRemove;
        }
    }
}
