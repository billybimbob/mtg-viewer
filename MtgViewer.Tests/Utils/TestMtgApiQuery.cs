using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Model;

using MtgViewer.Data;
using MtgViewer.Services;
using MtgViewer.Services.Search;

namespace MtgViewer.Tests.Utils;

public class TestMtgApiQuery : IMtgQuery
{
    private readonly TestCardService _testCards;
    private readonly IMtgQuery _mtgQuery;
    private readonly MtgApiFlipQuery _flipQuery;

    private IAsyncEnumerable<ICard>? _flipCards;

    public TestMtgApiQuery(
        TestCardService testCards,
        PageSize pageSize,
        LoadingProgress loadingProgress,
        ILogger<MtgApiFlipQuery> logger)
    {
        _testCards = testCards;
        _flipQuery = new MtgApiFlipQuery(testCards, pageSize, logger);
        _mtgQuery = new MtgApiQuery(testCards, _flipQuery, pageSize, loadingProgress);
    }

    public IAsyncEnumerable<ICard> SourceCards => _testCards.Cards;

    public IAsyncEnumerable<ICard> FlipCards =>
        _flipCards ??= SourceCards.Where(c => _flipQuery.HasFlip(c.Name));

    public IMtgCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
        => _mtgQuery.Where(predicate);

    public IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds)
        => _mtgQuery.CollectionAsync(multiverseIds);

    public Task<Card?> FindAsync(string id, CancellationToken cancel = default)
        => _mtgQuery.FindAsync(id, cancel);
}
