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
        public IReadOnlyList<AmountModel> Amounts { get; set; }


        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task OnPostCardAsync()
        {
            Matches = await _mtgFetch.MatchAsync(Card);

            if (!Matches.Any())
            {
                ErrorMessage = "No Matches were found";
            }
            else
            {
                Amounts = Matches
                    .Select(m => new AmountModel{ Id = m.MultiverseId })
                    .ToList();
            }
        }



        public async Task<IActionResult> OnPostAmountsAsync()
        {
            var newAmounts = await FilterNewAmountsAsync(Amounts);

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


        private async Task<IEnumerable<AmountModel>> FilterNewAmountsAsync(IEnumerable<AmountModel> amounts)
        {
            var picked = amounts.Where(a => a.Amount > 0);

            if (!picked.Any())
            {
                return picked;
            }

            var pickedIds = picked.Select(a => a.Id).ToArray();

            var inContext = (await _dbContext.Cards
                .Select(c => c.MultiverseId)
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

                card.Amounts.Add(new CardAmount
                {
                    Amount = info.Amount
                });

                _dbContext.Cards.Add(card);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e.ToString());
            }
        }

    }
}
