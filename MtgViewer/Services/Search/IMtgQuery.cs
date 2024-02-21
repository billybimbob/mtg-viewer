using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using MtgViewer.Data;

namespace MtgViewer.Services.Search;

public interface IMtgQuery
{
    bool HasFlip(string cardName);

    Task<OffsetList<Card>> SearchAsync(IMtgSearch search, CancellationToken cancel = default);

    IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds, CancellationToken cancel = default);

    Task<Card?> FindAsync(string id, CancellationToken cancel = default);
}
