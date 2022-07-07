using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Model;

using MtgViewer.Data;
using MtgViewer.Services;
using MtgViewer.Services.Search;

namespace MtgViewer.Tests.Utils;

public class TestMtgApiQuery : IMtgQuery
{
    private readonly IMtgQuery _mtgQuery;
    private readonly TestCardService _testCards;

    private IAsyncEnumerable<ICard>? _flipCards;

    public TestMtgApiQuery(
        TestCardService testCards,
        PageSize pageSize,
        LoadingProgress loadingProgress,
        ILogger<MtgApiQuery> logger)
    {
        _mtgQuery = new MtgApiQuery(testCards, pageSize, loadingProgress, logger);
        _testCards = testCards;
    }

    public IAsyncEnumerable<ICard> SourceCards => _testCards.Cards;

    public IAsyncEnumerable<ICard> FlipCards =>
        _flipCards ??= SourceCards.Where(c => _mtgQuery.HasFlip(c.Name));

    public bool HasFlip(string cardName)
        => _mtgQuery.HasFlip(cardName);

    public Task<OffsetList<Card>> SearchAsync(IMtgSearch search, CancellationToken cancel = default)
        => _mtgQuery.SearchAsync(search, cancel);

    public IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds)
        => _mtgQuery.CollectionAsync(multiverseIds);

    public Task<Card?> FindAsync(string id, CancellationToken cancel = default)
        => _mtgQuery.FindAsync(id, cancel);
}
