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

public class DetailsModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly int _pageSize;

    public DetailsModel(CardDbContext dbContext, PageSizes pageSizes)
    {
        _dbContext = dbContext;
        _pageSize = pageSizes.GetPageModelSize<DetailsModel>();
    }


    public Seek Seek { get; private set; }

    public Box Box { get; private set; } = default!;

    public int NumberOfCards { get; private set; }

    public bool HasMore =>
        Box is null ? false : Box.Cards.Sum(a => a.NumCopies) < NumberOfCards;


    public async Task<IActionResult> OnGetAsync(
        int id, 
        int? seek,
        bool backtrack,
        string? cardId,
        CancellationToken cancel)
    {
        if (await GetCardJumpAsync(cardId, id, cancel) is int cardJump)
        {
            return RedirectToPage(new { seek = cardJump });
        }

        var cards = await BoxCards(id)
            .SeekBy(_pageSize, backtrack)
            .WithSource<Amount>()
            .WithKey(seek)
            .ToSeekListAsync(cancel);

        if (!cards.Any())
        {
            return await EmptyBoxAsync(id, cancel);
        }

        NumberOfCards = await NumberOfCardsAsync.Invoke(_dbContext, id, cancel);

        Seek = (Seek)cards.Seek;

        Box = (Box)cards.First().Location;

        return Page();
    }


    private async Task<IActionResult> EmptyBoxAsync(int id, CancellationToken cancel)
    {
        var box = await BoxAsync.Invoke(_dbContext, id, cancel);

        if (box == default)
        {
            return NotFound();
        }

        Box = box;

        return Page();
    }


    private static readonly Func<CardDbContext, int, CancellationToken, Task<Box?>> BoxAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int boxId, CancellationToken _) =>
            dbContext.Boxes
                .Include(b => b.Bin)
                .SingleOrDefault(b => b.Id == boxId));


    private async Task<int?> GetCardJumpAsync(string? cardId, int boxId, CancellationToken cancel)
    {
        if (cardId is null)
        {
            return null;
        }

        var card = await CardJumpAsync.Invoke(_dbContext, cardId, boxId, cancel);
        if (card is null)
        {
            return null;
        }

        return await BoxCards(boxId)
            .Before(card)
            .Select(a => a.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .LastOrDefaultAsync(cancel);
    }


    private IQueryable<Amount> BoxCards(int boxId)
    {
        return _dbContext.Amounts
            .Where(a => a.Location is Box && a.LocationId == boxId)

            .Include(a => a.Card)
            .Include(a => a.Location)
                .ThenInclude(l => (l as Box)!.Bin)

            .OrderBy(a => a.Card.Name)
                .ThenBy(a => a.Card.SetName)
                .ThenBy(a => a.NumCopies)
                .ThenBy(a => a.Id)

            .AsNoTrackingWithIdentityResolution();
    }


    private static readonly Func<CardDbContext, string, int, CancellationToken, Task<Amount?>> CardJumpAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, int boxId, CancellationToken _) =>

            dbContext.Amounts
                .Include(a => a.Card)
                .OrderBy(a => a.Id)
                .SingleOrDefault(a =>
                    a.Location is Box && a.LocationId == boxId && a.CardId == cardId));


    private static readonly Func<CardDbContext, int, CancellationToken, Task<int>> NumberOfCardsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int boxId, CancellationToken _) =>

            dbContext.Boxes
                .Where(b => b.Id == boxId)
                .SelectMany(b => b.Cards)
                .Sum(a => a.NumCopies));
}