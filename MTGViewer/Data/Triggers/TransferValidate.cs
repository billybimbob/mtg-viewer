using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntityFrameworkCore.Triggered;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


namespace MTGViewer.Data.Triggers
{
    public class TransferValidate : IBeforeSaveTrigger<Transfer>
    {
        private readonly CardDbContext _dbContext;
        private readonly ILogger<TradeValidate> _logger;

        public TransferValidate(CardDbContext dbContext, ILogger<TradeValidate> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }


        public async Task BeforeSave(ITriggerContext<Transfer> trigContext, CancellationToken cancel)
        {
            if (trigContext.ChangeType is not ChangeType.Added)
            {
                return;
            }

            var transfer = trigContext.Entity;

            List<int> searchIds = new();
            List<Deck> decks = new();

            if (transfer.To is not null)
            {
                decks.Add(transfer.To);
            }
            else if (transfer.ToId != default)
            {
                searchIds.Add(transfer.ToId);
            }

            if (transfer is Trade trade)
            {
                if (trade.From is not null)
                {
                    decks.Add(trade.From);
                }
                else if (trade.FromId != default)
                {
                    searchIds.Add(trade.FromId);
                }
            }

            if (searchIds.Any())
            {
                var searchedDecks = await _dbContext.Decks
                    .Where(d => searchIds.Contains(d.Id))
                    .AsNoTracking()
                    .ToListAsync();

                decks.AddRange(searchedDecks);
            }

            foreach (var deck in decks)
            {
                transfer.Decks.Add(deck);
            }
        }
    }
}