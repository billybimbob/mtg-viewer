using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services;


namespace MTGViewer.Pages.Cards
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly ILogger<CreateModel> _logger;
        private readonly CardDbContext _context;
        private readonly MTGFetchService _fetch;

        public CreateModel(CardDbContext context, MTGFetchService fetch, ILogger<CreateModel> logger)
        {
            _logger = logger;
            _context = context;
            _fetch = fetch;
        }

        public Card Card { get; private set; }

        public IReadOnlyList<Card> Matches { get; private set; }

        [BindProperty]
        public IList<AmountModel> Amounts { get; set; }


        public class AmountModel
        {
            [HiddenInput]
            public string Id { get; set; }

            [Range(0, int.MaxValue)]
            public int Amount { get; set; }
        }


        public IActionResult OnGet()
        {
            _logger.LogInformation("on get");
            return Page();
        }


        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task<IActionResult> OnPostAsync()
        {
            _logger.LogInformation("on post");

            if (Amounts is not null && Amounts.Any())
            {
                return await FromPickedAsync();
            }
            else
            {
                return await FromPostAsync();
            }
        }


        private async Task<IActionResult> FromPostAsync()
        {
            Card = new Card();

            if (await TryUpdateModelAsync(Card, "card"))
            {
                Matches = await _fetch.MatchAsync(Card);
                Amounts = Matches
                    .Select(m => new AmountModel{ Id = m.MultiverseId })
                    .ToList();
            }

            // if (Matches?.Count() == 1)
            // {
            //     Picked = Matches.First().Id;
            //     return await FromPicked();
            // }

            return Page();
        }

        private async Task<IActionResult> FromPickedAsync()
        {
            var newAmounts = await GetNewAmountsAsync();

            if (!newAmounts.Any())
            {
                _logger.LogError("no amounts were set");
                return Page();
            }

            await AddNewCardsAsync(newAmounts);

            return RedirectToPage("./Index");
        }


        private async Task<IEnumerable<AmountModel>> GetNewAmountsAsync()
        {
            var picked = Amounts.Where(a => a.Amount > 0);

            if (!picked.Any())
            {
                return Enumerable.Empty<AmountModel>();
            }

            var pickedIds = picked.Select(a => a.Id).ToArray();

            var inContext = (await _context.Cards
                .Select(c => c.Id)
                .Where(id => pickedIds.Contains(id))
                .AsNoTracking()
                .ToListAsync())
                .ToHashSet();

            return picked.Where(a => !inContext.Contains(a.Id));
        }

        
        private async Task AddNewCardsAsync(IEnumerable<AmountModel> newAmounts)
        {
            foreach(var info in newAmounts)
            {
                var card = await _fetch.FindAsync(info.Id);

                if (card is null)
                {
                    _logger.LogError($"{info.Id} failed to fail correct card");
                    continue;
                }

                var amountEntry = new CardAmount{ Amount = info.Amount };
                card.Amounts.Add(amountEntry);

                _context.Cards.Add(card);
            }

            await _context.SaveChangesAsync();
        }

    }
}
