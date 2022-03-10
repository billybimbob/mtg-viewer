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


    public SeekList<CardPreview> Cards { get; private set; } = SeekList<CardPreview>.Empty;

    public bool HasExcess =>
        Cards.Any()
            || Cards.Seek.Previous is not null
            || Cards.Seek.Next is not null;


    public async Task<IActionResult> OnGetAsync(
        string? seek, 
        bool backtrack, 
        string? cardId,
        CancellationToken cancel)
    {
        if (await GetCardJumpAsync(cardId, cancel) is string cardJump)
        {
            return RedirectToPage(new { seek = cardJump });
        }

        Cards = await ExcessCards()
            .SeekBy(_pageSize, backtrack)
            .WithSource<Card>()
            .WithKey(seek)
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
            .WithSelect<Card, CardPreview>()
            .Before(card)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .LastOrDefaultAsync(cancel);
    }


    private IQueryable<CardPreview> ExcessCards()
    {
        return _dbContext.Cards
            .Where(c => c.Amounts
                .Any(a => a.Location is Excess))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(c => new CardPreview
            {
                Id = c.Id,
                Name = c.Name,
                ManaCost = c.ManaCost,
                SetName = c.SetName,

                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,

                Total = c.Amounts
                    .Where(a => a.Location is Excess)
                    .Sum(a => a.NumCopies)
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