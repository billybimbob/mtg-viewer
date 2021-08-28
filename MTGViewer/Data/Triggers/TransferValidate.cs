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
            var decks = await GetTransferDecks(transfer);

            // foreach (var deck in decks)
            // {
            //     transfer.Decks.Add(deck);
            // }
        }


        private async Task<IReadOnlyList<Deck>> GetTransferDecks(Transfer transfer)
        {
            List<Deck> decks = new();
            List<int> searchIds = new();

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

            if (transfer.To is not null)
            {
                decks.Add(transfer.To);
            }
            else if (transfer.ToId is int toId)
            {
                searchIds.Add(toId);
            }

            if (searchIds.Any())
            {
                var searchedDecks = await _dbContext.Decks
                    .Where(d => searchIds.Contains(d.Id))
                    .AsNoTracking()
                    .ToListAsync();

                decks.AddRange(searchedDecks);
            }

            return decks;
        }
    }
}