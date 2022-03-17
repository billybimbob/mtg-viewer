using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;
using MTGViewer.Data;

namespace MTGViewer.Services;


public interface IMTGQuery
{
    IMTGCardSearch Where(Expression<Func<CardQuery, bool>> predicate);

    IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds);

    ValueTask<Card?> FindAsync(string id, CancellationToken cancel = default);
}


public interface IMTGCardSearch
{
    IMTGCardSearch Where(Expression<Func<CardQuery, bool>> predicate);

    ValueTask<OffsetList<Card>> SearchAsync(CancellationToken cancel = default);
}
