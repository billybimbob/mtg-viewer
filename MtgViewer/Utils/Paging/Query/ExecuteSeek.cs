using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Paging.Query;

internal static class ExecuteSeek<TEntity> where TEntity : class
{
    public static async Task<SeekList<TEntity>> ToSeekListAsync(
        IQueryable<TEntity> query,
        CancellationToken cancel)
    {
        if (FindSeekVisitor.Instance.Visit(query.Expression) is not SeekExpression seekExpression)
        {
            return await GetMissingSeekListAsync(query, cancel);
        }

        var addLookAhead = new AddSeekVisitor(
            seekExpression.Origin.Value, seekExpression.Direction, seekExpression.Take + 1);

        var items = await query.Provider
            .CreateQuery<TEntity>(addLookAhead.Visit(query.Expression))
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        bool hasOrigin = seekExpression.Origin.Value is not null;

        bool lookAhead = items.Count == seekExpression.Take + 1;

        if (lookAhead)
        {
            // potential issue with extra items tracked that are not actually returned
            // keep eye on

            items.RemoveAt(items.Count - 1);
        }

        var seek = new Seek<TEntity>(
            items, seekExpression.Direction, hasOrigin, seekExpression.Take, lookAhead);

        return new SeekList<TEntity>(seek, items);
    }

    private static async Task<SeekList<TEntity>> GetMissingSeekListAsync(
        IQueryable<TEntity> query,
        CancellationToken cancel)
    {
        var items = await query.ToListAsync(cancel).ConfigureAwait(false);

        var seek = new Seek<TEntity>(
            items,
            SeekDirection.Forward,
            hasOrigin: false,
            targetSize: null,
            lookAhead: false);

        return new SeekList<TEntity>(seek, items);
    }
}
