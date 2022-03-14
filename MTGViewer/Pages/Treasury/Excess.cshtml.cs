using System;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;

public class ExcessModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;

    public ExcessModel(PageSizes pageSizes, CardDbContext dbContext)
    {
        _pageSize = pageSizes.GetPageModelSize<ExcessModel>();
        _dbContext = dbContext;
    }


    public SeekList<CardCopies> Cards { get; private set; } = SeekList<CardCopies>.Empty;

    public bool HasExcess =>
        Cards.Any() || Cards.Seek is not { Previous: null, Next: null };


    public async Task<IActionResult> OnGetAsync(
        string? seek, 
        SeekDirection direction,
        string? cardId,
        CancellationToken cancel)
    {
        if (await GetCardJumpAsync(cardId, cancel) is string cardJump)
        {
            return RedirectToPage(new { seek = cardJump });
        }

        Cards = await ExcessCards()
            .SeekBy(seek, direction)
            .OrderBy<Card>()
            .Take(_pageSize)
            .ToSeekListAsync(cancel);

        return Page();
    }


    private async Task<string?> GetCardJumpAsync(string? id, CancellationToken cancel)
    {
        if (id is null)
        {
            return default;
        }

        var card = await CardJumpAsync.Invoke(_dbContext, id, cancel);
        if (card is null)
        {
            return default;
        }

        return await ExcessCards()
            .WithSelect<Card, CardCopies>()
            .Before(card)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .LastOrDefaultAsync(cancel);
    }


    private IQueryable<CardCopies> ExcessCards()
    {
        return _dbContext.Cards
            .Where(c => c.Amounts
                .Any(a => a.Location is Excess))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(c => new CardCopies
            {
                Id = c.Id,
                Name = c.Name,
                ManaCost = c.ManaCost,
                SetName = c.SetName,

                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,

                Copies = c.Amounts
                    .Where(a => a.Location is Excess)
                    .Sum(a => a.Copies)
            });
    }


    private static readonly Func<CardDbContext, string, CancellationToken, Task<Card?>> CardJumpAsync

        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, CancellationToken _) =>
            dbContext.Cards
                .Where(c => c.Amounts
                    .Any(a => a.Location is Excess))
                .OrderBy(c => c.Id)
                .SingleOrDefault(c => c.Id == cardId));
}