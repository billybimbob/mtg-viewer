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
            .SeekBy(b => b.Id, seek, _pageSize, backtrack)
            .ToSeekListAsync(cancel);

        if (!cards.Any())
        {
            return await EmptyBoxAsync(id, cancel);
        }

        NumberOfCards = await _dbContext.Boxes
            .Where(b => b.Id == id)
            .SelectMany(b => b.Cards)
            .SumAsync(a => a.NumCopies, cancel);

        Seek = cards.Seek;

        Box = (Box)cards.First().Location;

        return Page();
    }


    private async Task<IActionResult> EmptyBoxAsync(int id, CancellationToken cancel)
    {
        var box = await _dbContext.Boxes
            .Include(b => b.Bin)
            .SingleOrDefaultAsync(b => b.Id == id, cancel);

        if (box == default)
        {
            return NotFound();
        }

        Box = box;

        return Page();
    }


    private async Task<int?> GetCardJumpAsync(string? cardId, int boxId, CancellationToken cancel)
    {
        if (cardId is null)
        {
            return null;
        }

        var boxCards = BoxCards(boxId);

        var card = await boxCards
            .Where(a => a.CardId == cardId)
            .OrderBy(a => a.Id)
            .Select(a => a.Card)
            .SingleOrDefaultAsync(cancel);

        if (card is null)
        {
            return null;
        }

        return await boxCards
            .Before(card, a => a.Card)
            .Select(a => a.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .LastOrDefaultAsync(cancel);
    }


    private IQueryable<Amount> BoxCards(int boxId)
    {
        return _dbContext.Amounts
            .Where(a => a.Location is Box
                && a.LocationId == boxId)

            .Include(a => a.Card)
            .Include(a => a.Location)
                .ThenInclude(l => (l as Box)!.Bin)

            .OrderBy(a => a.Card.Name)
                .ThenBy(a => a.Card.SetName)
                .ThenBy(a => a.NumCopies)
                .ThenBy(a => a.Id)

            .AsNoTrackingWithIdentityResolution();
    }
}