using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Trades
{
    [Authorize]
    public class CounterModel : PageModel
    {
        private readonly CardDbContext _dbContext;

        public CounterModel(CardDbContext dbContext)
        {
            _dbContext = dbContext;
        }


        [BindProperty]
        public IReadOnlyCollection<Trade> Trades { get; private set; }

        public async Task OnGetAsync(int deckId)
        {
            Trades = await _dbContext.Trades
                .Where(TradeFilter.PendingFor(deckId))
                .Include(t => t.Card)
                .Include(t => t.FromUser)
                .Include(t => t.To)
                .AsSplitQuery()
                .ToListAsync();
        }

        public async Task OnPostAsync()
        {
            foreach(var trade in Trades)
            {
                _dbContext.Attach(trade);
                trade.IsCounter = !trade.IsCounter;
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}