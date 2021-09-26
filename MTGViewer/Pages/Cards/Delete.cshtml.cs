using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;


namespace MTGViewer.Pages.Cards
{
    [Authorize]
    public class DeleteModel : PageModel
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<DeleteModel> _logger;

        public DeleteModel(CardDbContext dbContext, ILogger<DeleteModel> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }


        public Card Card { get; private set; }

        public IReadOnlyList<Location> Locations { get; private set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id is null)
            {
                return NotFound();
            }

            Card = await _dbContext.Cards
                .Include(c => c.SuperTypes)
                .Include(c => c.Types)
                .Include(c => c.SubTypes)
                .Include(c => c.Amounts
                    .OrderBy(ca => ca.Location.Name))
                    .ThenInclude(ca => ca.Location)
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(c => c.Id == id);

            if (Card == default)
            {
                return NotFound();
            }

            Locations = Card.Amounts
                // group by to keep sorted order
                .GroupBy(ca => ca.Location,
                    (location, _) => location)
                .ToList();

            return Page();
        }


        public async Task<IActionResult> OnPostAsync(string id)
        {
            if (id is null)
            {
                return NotFound();
            }

            var card = await _dbContext.Cards.FindAsync(id);

            if (card is null)
            {
                return RedirectToPage("./Index");
            }

            _dbContext.Cards.Remove(card);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e.ToString());
            }

            return RedirectToPage("./Index");
        }
    }
}