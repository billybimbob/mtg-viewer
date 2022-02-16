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


    public Seek<Amount> Seek { get; private set; }

    public Box Box { get; private set; } = default!;

    public int NumberOfCards { get; private set; }

    public bool HasMore =>
        Box is null ? false : Box.Cards.Sum(a => a.NumCopies) < NumberOfCards;


    public async Task<IActionResult> OnGetAsync(
        int id, 
        int? seek,
        int? index,
        bool backtrack,
        string? cardId,
        CancellationToken cancel)
    {
        if (await GetCardJumpAsync(cardId, id, cancel) is (int cardJump, int cardIndex))
        {
            return RedirectToPage(new { seek = cardJump, index = cardIndex });
        }

        var cards = await BoxCards(id)
            .ToSeekListAsync(index, _pageSize, seek, backtrack, cancel);

        if (!cards.Any())
        {
            return NotFound();
        }

        NumberOfCards = await _dbContext.Boxes
            .Where(b => b.Id == id && !b.IsExcess)
            .SelectMany(b => b.Cards)
            .SumAsync(a => a.NumCopies, cancel);

        Seek = cards.Seek;

        Box = (Box)cards.First().Location;

        return Page();
    }


    private async Task<SeekJump<int>> GetCardJumpAsync(string? cardId, int boxId, CancellationToken cancel)
    {
        if (cardId is null)
        {
            return default;
        }

        var cardName = await BoxCards(boxId)
            .Where(a => a.CardId == cardId)
            .Select(a => a.Card.Name)
            .FirstOrDefaultAsync(cancel);

        if (cardName is null)
        {
            return default;
        }

        var options = await BoxCards(boxId)
            .Where(a => a.Card.Name.CompareTo(cardName) < 0)
            .Select(a => a.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .ToListAsync(cancel);

        return new SeekJump<int>(options.ElementAtOrDefault(^1), options.Count - 1);
    }


    private IQueryable<Amount> BoxCards(int boxId)
    {
        return _dbContext.Amounts
            .Where(a => a.Location is Box
                && !(a.Location as Box)!.IsExcess
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