using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Model;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Services.Search;

namespace MTGViewer.Tests.Utils;

public class TestMtgApiQuery : IMTGQuery
{
    private readonly TestCardService _testCards;
    private readonly IMTGQuery _mtgQuery;
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

    public IMTGCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
        => _mtgQuery.Where(predicate);

    public IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds)
        => _mtgQuery.CollectionAsync(multiverseIds);

    public Task<Card?> FindAsync(string id, CancellationToken cancel = default)
        => _mtgQuery.FindAsync(id, cancel);
}
