using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using MtgViewer.Data;

namespace MtgViewer.Services.Search;

public interface IMtgQuery
{
    IMtgCardSearch Where(Expression<Func<CardQuery, bool>> predicate);

    IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds);

    Task<Card?> FindAsync(string id, CancellationToken cancel = default);
}

public interface IMtgCardSearch
{
    IMtgCardSearch Where(Expression<Func<CardQuery, bool>> predicate);

    Task<OffsetList<Card>> SearchAsync(CancellationToken cancel = default);
}
