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


    public async Task OnGetAsync(int? seek, SeekDirection direction, CancellationToken cancel)
    {
        var boxes = await BoxesForViewing()
            .SeekBy(seek, direction)
            .OrderBy<Box>()
            .Take(_pageSize)
            .ToSeekListAsync(cancel);

        Bins = boxes
            .GroupBy(b => b.Bin,
                (bin, boxPreviews) =>
                    bin with { Boxes = boxPreviews })
            .ToList();

        Seek = (Seek)boxes.Seek;

        HasExcess = await HasExcessAsync.Invoke(_dbContext, cancel);
    }


    private IQueryable<BoxPreview> BoxesForViewing()
    {
        return _dbContext.Boxes

            .OrderBy(b => b.Bin.Name)
                .ThenBy(b => b.BinId)
                .ThenBy(b => b.Name)
                .ThenBy(b => b.Id)

            .Select(b => new BoxPreview
            {
                Id = b.Id,
                Name = b.Name,

                Bin = new BinPreview
                {
                    Id = b.BinId,
                    Name = b.Bin.Name
                },

                Appearance = b.Appearance,
                Capacity = b.Capacity,
                TotalCards = b.Cards.Sum(a => a.Copies),

                Cards = b.Cards
                    .OrderBy(a => a.Card.Name)
                        .ThenBy(a => a.Card.SetName)
                        .ThenBy(a => a.Copies)

                    .Take(_pageSize)
                    .Select(a => new StorageLink
                    {
                        Id = a.CardId,
                        Name = a.Card.Name,

                        SetName = a.Card.SetName,
                        ManaCost = a.Card.ManaCost,

                        Copies = a.Copies
                    })
            });
    }


    private static readonly Func<CardDbContext, CancellationToken, Task<bool>> HasExcessAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, CancellationToken _) =>
            dbContext.Amounts
                .Any(a => a.Location is Excess));
}
