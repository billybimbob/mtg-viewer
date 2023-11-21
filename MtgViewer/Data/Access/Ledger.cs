using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MtgViewer.Data.Projections;

namespace MtgViewer.Data.Access;

public sealed class Ledger : ILedger
{
    private readonly IDbContextFactory<CardDbContext> _dbContextFactory;

    public Ledger(IDbContextFactory<CardDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<RecentTransaction>> GetRecentChangesAsync(int size, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await RecentTransactionsAsync.Invoke(dbContext, size).ToListAsync(cancellationToken);
    }

    #region Database Queries

    private static readonly Func<CardDbContext, int, IAsyncEnumerable<RecentTransaction>> RecentTransactionsAsync
        = EF.CompileAsyncQuery((CardDbContext db, int size)
            => db.Transactions
                .Where(t => t.Changes
                    .Any(c => c.From is Box
                        || c.From is Excess
                        || c.To is Box
                        || c.To is Excess))

                .OrderByDescending(t => t.AppliedAt)
                .Take(size)
                .Select(t => new RecentTransaction
                {
                    AppliedAt = t.AppliedAt,
                    Copies = t.Changes.Sum(c => c.Copies),

                    Changes = t.Changes
                        .Where(c => c.From is Box
                            || c.From is Excess
                            || c.To is Box
                            || c.To is Excess)

                        .OrderBy(c => c.Card.Name)
                        .Take(size)
                        .Select(c => new RecentChange
                        {
                            FromStorage = c.From is Box || c.From is Excess,
                            ToStorage = c.To is Box || c.To is Excess,
                            CardName = c.Card.Name,
                        }),
                }));

    #endregion
}
