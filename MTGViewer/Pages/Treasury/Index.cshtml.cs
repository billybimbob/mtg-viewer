using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;


public class IndexModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;

    public IndexModel(PageSizes pageSizes, CardDbContext dbContext)
    {
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
        _dbContext = dbContext;
    }


    public IReadOnlyList<BinPreview> Bins { get; private set; } = Array.Empty<BinPreview>();

    public Seek Seek { get; private set; }

    public bool HasExcess { get; private set; }


    public async Task OnGetAsync(int? seek, bool backtrack, CancellationToken cancel)
    {
        var boxes = await BoxesForViewing()
            .SeekBy(b => b.Id, seek, _pageSize, backtrack)
            .ToSeekListAsync(cancel);

        Bins = boxes
            .GroupBy(b => (Name: b.BinName, Id: b.BinId),
                (bin, boxes) => new BinPreview(bin.Id, bin.Name, boxes))
            .ToList();

        Seek = boxes.Seek;

        HasExcess = await _dbContext.Amounts
            .AnyAsync(a => a.Location is Excess, cancel);

    }


    private IQueryable<BoxPreview> BoxesForViewing()
    {
        return _dbContext.Boxes

            .Include(b => b.Cards
                .OrderBy(a => a.Card.Name)
                    .ThenBy(a => a.Card.SetName)
                    .ThenBy(a => a.NumCopies)
                .Take(_pageSize))

            .Select(b => new BoxPreview
            {
                Id = b.Id,
                Name = b.Name,

                BinId = b.BinId,
                BinName = b.Bin.Name,

                Capacity = b.Capacity,
                TotalCards = b.Cards.Sum(a => a.NumCopies),

                Cards = b.Cards
                    .Select(a => new BoxCard
                    {
                        CardId = a.CardId,
                        CardName = a.Card.Name,
                        CardManaCost = a.Card.ManaCost,
                        CardSetName = a.Card.SetName,

                        NumCopies = a.NumCopies
                    })
                    .OrderBy(a => a.CardName)
                        .ThenBy(a => a.CardSetName)
                        .ThenBy(a => a.NumCopies)
                    .Take(_pageSize)
            })

            .OrderBy(b => b.BinName)
                .ThenBy(b => b.BinId)
                .ThenBy(b => b.Name)
                .ThenBy(b => b.Id)

            .AsNoTrackingWithIdentityResolution();
    }
}
