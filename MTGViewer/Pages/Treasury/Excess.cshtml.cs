using System;
using System.Collections.Paging;
using System.Linq;
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


    public SeekList<Card> Cards { get; private set; } = SeekList<Card>.Empty();

    public bool HasExcess =>
        Cards.Any()
            || Cards.Seek.Previous is not null
            || Cards.Seek.Next is not null;


    public async Task<IActionResult> OnGetAsync(
        string? seek, 
        bool backTrack, 
        string? cardId, 
        CancellationToken cancel)
    {
        if (seek is null
            && await GetCardSeekAsync(cardId, cancel) is string cardSeek)
        {
            return RedirectToPage(new { seek = cardSeek });
        }

        Cards = await GetCardsAsync(seek, backTrack, cancel);

        return Page();
    }


    private async Task<string?> GetCardSeekAsync(string? cardId, CancellationToken cancel)
    {
        if (cardId is null)
        {
            return null;
        }

        var cardName = await ExcessCards()
            .Where(c => c.Id == cardId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancel);

        if (cardName is null)
        {
            return null;
        }

        return await ExcessCards()
            .Where(c => c.Name.CompareTo(cardName) < 0)
            .Select(c => c.Id)

            .AsAsyncEnumerable()
            .Where((id, i) => i % _pageSize == _pageSize - 1)
            .LastOrDefaultAsync(cancel);
    }


    private IQueryable<Card> ExcessCards()
    {
        return _dbContext.Cards
            .Where(c => c.Amounts
                .Any(a => a.Location is Box
                    && (a.Location as Box)!.IsExcess))

            .Include(c => c.Amounts // unbounded, keep eye on
                .Where(a => a.Location is Box 
                    && (a.Location as Box)!.IsExcess))
            
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .AsNoTrackingWithIdentityResolution();
    }


    private async Task<SeekList<Card>> GetCardsAsync(
        string? seek, bool backTrack, CancellationToken cancel)
    {
        var excessCards = ExcessCards();

        if (seek is null)
        {
            return await excessCards
                .ToSeekListAsync(SeekPosition.Start, _pageSize, cancel);
        }

        var card = await _dbContext.Cards
            .Where(c => c.Amounts
                .Any(a => a.Location is Box
                    && (a.Location as Box)!.IsExcess))
            .OrderBy(c => c.Id)
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == seek, cancel);

        if (card == default)
        {
            return await excessCards
                .ToSeekListAsync(SeekPosition.Start, _pageSize, cancel);
        }

        return backTrack
            ? await excessCards
                .ToSeekListAsync(c =>
                    c.Name == card.Name
                        && c.SetName == card.SetName
                        && c.Id.CompareTo(c.Id) < 0

                    || c.Name == card.Name
                        && c.SetName.CompareTo(card.SetName) < 0

                    || c.Name.CompareTo(card.Name) < 0,

                    SeekPosition.End, _pageSize, cancel)

            : await excessCards
                .ToSeekListAsync(c =>
                    c.Name == card.Name
                        && c.SetName == card.SetName
                        && c.Id.CompareTo(c.Id) > 0

                    || c.Name == card.Name
                        && c.SetName.CompareTo(card.SetName) > 0

                    || c.Name.CompareTo(card.Name) > 0,

                    SeekPosition.Start, _pageSize, cancel);
    }
}