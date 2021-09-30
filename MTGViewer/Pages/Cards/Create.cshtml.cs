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
        private readonly CardDbContext _dbContext;
        private readonly MTGFetchService _mtgFetch;
        private readonly ISharedStorage _sharedStorage;
        private readonly ILogger<CreateModel> _logger;


        public CreateModel(
            CardDbContext dbContext, 
            MTGFetchService mtgFetch, 
            ISharedStorage sharedStorage,
            ILogger<CreateModel> logger)
        {
            _dbContext = dbContext;
            _mtgFetch = mtgFetch;
            _sharedStorage = sharedStorage;
            _logger = logger;
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


        public void OnGet()
        {
        }


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
            var fetchedCards = await Task.WhenAll(
                newAmounts.Select(am => _mtgFetch.FindAsync(am.Id)));

            var validResults = fetchedCards
                .Where(c => c != null);

            _dbContext.Cards.AddRange(validResults);

            var newCards = validResults
                .Zip(newAmounts.Select(am => am.Amount),
                    (card, numCopies) => new CardReturn(card, numCopies))
                .ToList();

            try
            {
                await _dbContext.SaveChangesAsync();

                if (newCards.Any())
                {
                    await _sharedStorage.ReturnAsync(newCards);
                }
            }
            catch (DbUpdateException e)
            {
                _logger.LogError(e.ToString());
            }
        }

    }
}
