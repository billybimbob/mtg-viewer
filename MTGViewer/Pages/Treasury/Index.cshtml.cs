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
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public IndexModel(CardDbContext dbContext, PageSize pageSize)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    public IReadOnlyList<BinPreview> Bins { get; private set; } = Array.Empty<BinPreview>();

    public Seek Seek { get; private set; }

    public bool HasExcess { get; private set; }

    public async Task OnGetAsync(int? seek, SeekDirection direction, CancellationToken cancel)
    {
        var boxes = await BoxesForViewing()
            .SeekBy(seek, direction)
            .OrderBy<Box>()
            .Take(_pageSize.Current)
            .ToSeekListAsync(cancel);

        Bins = boxes
            .GroupBy(b => b.Bin,
                (bin, boxPreviews) =>
                    bin with { Boxes = boxPreviews })
            .ToList();

        Seek = (Seek)boxes.Seek;

        HasExcess = await _dbContext.Excess.AnyAsync(cancel);
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
                Held = b.Holds.Sum(h => h.Copies),

                Cards = b.Holds
                    .OrderBy(h => h.Card.Name)
                        .ThenBy(h => h.Card.SetName)
                        .ThenBy(h => h.Copies)

                    .Take(_pageSize.Current)
                    .Select(h => new LocationLink
                    {
                        Id = h.CardId,
                        Name = h.Card.Name,

                        SetName = h.Card.SetName,
                        ManaCost = h.Card.ManaCost,

                        Held = h.Copies
                    })
            });
    }
}
