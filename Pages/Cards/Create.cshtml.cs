using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MTGViewer.Data;
using MTGViewer.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MTGViewer.Pages.Cards
{
    public class CreateModel : PageModel
    {
        private readonly ILogger<CreateModel> _logger;
        private readonly MTGCardContext _context;
        private readonly MTGFetchService _fetch;

        public CreateModel(MTGCardContext context, MTGFetchService fetch, ILogger<CreateModel> logger)
        {
            _logger = logger;
            _context = context;
            _fetch = fetch;
        }

        public Card Card { get; private set; }
        public IEnumerable<Card> Matches { get; private set; }

        [BindProperty]
        public string Picked { get; set; }


        public IActionResult OnGet()
        {
            _logger.LogInformation("on get");
            return Page();
        }


        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task<IActionResult> OnPostAsync()
        {
            _logger.LogInformation("on post");

            if (!string.IsNullOrEmpty(Picked))
            {
                return await FromPicked();
            }
            else
            {
                return await FromPost();
            }
        }


        private async Task<IActionResult> FromPost()
        {
            Card = new Card();

            if (await TryUpdateModelAsync(Card, "card"))
            {
                Matches = await _fetch.MatchAsync(Card);
            }

            if (Matches?.Count() == 1)
            {
                Picked = Matches.First().Id;
                return await FromPicked();
            }

            return Page();
        }

        private async Task<IActionResult> FromPicked()
        {
            _logger.LogInformation($"picked {Picked}");

            bool inContext = await _context.Cards
                .Where(c => c.Id == Picked)
                .AnyAsync();

            if (!inContext)
            {
                // not great since 2 gets from fetch service for a single create task
                Card = await _fetch.GetIdAsync(Picked);

                _context.Cards.Add(Card);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }

    }
}
