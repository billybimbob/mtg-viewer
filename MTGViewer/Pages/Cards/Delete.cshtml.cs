using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System.Threading.Tasks;

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

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id is null)
            {
                return NotFound();
            }

            Card = await _dbContext.Cards.FindAsync(id);

            if (Card is null)
            {
                return NotFound();
            }

            return Page();
        }


        public async Task<IActionResult> OnPostAsync(string id)
        {
            if (id is null)
            {
                return NotFound();
            }

            Card = await _dbContext.Cards.FindAsync(id);

            if (Card is null)
            {
                return RedirectToPage("./Index");
            }

            _dbContext.Cards.Remove(Card);

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
