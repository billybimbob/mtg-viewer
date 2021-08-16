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
        private readonly CardDbContext _dbContext;
        private readonly MTGFetchService _mtgFetch;

        public CreateModel(CardDbContext dbContext, MTGFetchService mtgFetch, ILogger<CreateModel> logger)
        {
            _logger = logger;
            _dbContext = dbContext;
            _mtgFetch = mtgFetch;
        }


        public string ErrorMessage { get; private set; }

        [BindProperty]
        public Card Card { get; set; }

        public class AmountModel
        {
            [HiddenInput]
            public string Id { get; set; }

            [Range(0, int.MaxValue)]
            public int Amount { get; set; }
        }

        [BindProperty]
        public IReadOnlyList<Card> Matches { get; set; }

        [BindProperty]
        public IList<AmountModel> Amounts { get; set; }



        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task OnPostCardAsync()
        {
            var matches = await _mtgFetch.MatchAsync(Card);

            if (!matches.Any())
            {
                ErrorMessage = "No Matches were found";
            }
            else
            {
                Matches = matches;
                Amounts = Matches
                    .Select(m => new AmountModel{ Id = m.MultiverseId })
                    .ToList();
            }
        }



        public async Task<IActionResult> OnPostAmountsAsync()
        {
            var newAmounts = await GetNewAmountsAsync();

            if (!newAmounts.Any())
            {
                ErrorMessage = "No Amounts were Specified";
                return Page();
            }
            else
            {
                await AddNewCardsAsync(newAmounts);
                return RedirectToPage("./Index");
            }
        }


        private async Task<IEnumerable<AmountModel>> GetNewAmountsAsync()
        {
            var picked = Amounts.Where(a => a.Amount > 0);

            if (!picked.Any())
            {
                return Enumerable.Empty<AmountModel>();
            }

            var pickedIds = picked.Select(a => a.Id).ToArray();

            var inContext = (await _dbContext.Cards
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
                var card = await _mtgFetch.FindAsync(info.Id);

                if (card is null)
                {
                    _logger.LogError($"{info.Id} failed to fail correct card");
                    continue;
                }

                var amountEntry = new CardAmount{ Amount = info.Amount };
                card.Amounts.Add(amountEntry);

                _dbContext.Cards.Add(card);
            }

            await _dbContext.SaveChangesAsync();
        }

    }
}
