using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;

namespace MTGViewer.Pages.Treasury;

public class DetailsModel : PageModel
{
    private readonly CardDbContext _dbContext; 

    public DetailsModel(CardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Box Box { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var box = await _dbContext.Boxes
            .Include(b => b.Bin)

            .Include(b => b.Cards // unbounded, keep eye on
                .OrderBy(a => a.Card.Name)
                    .ThenBy(a => a.Card.SetName)
                    .ThenBy(a => a.NumCopies))
                .ThenInclude(a => a.Card)

            .AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync(b => !b.IsExcess && b.Id == id, cancel);

        if (box == default)
        {
            return NotFound();
        }

        Box = box;

        return Page();
    }
}