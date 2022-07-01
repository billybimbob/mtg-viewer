using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using MtgApiManager.Lib.Service;

using MtgViewer.Data;

namespace MtgViewer.Services.Search;

public sealed class MtgApiQuery : IMtgQuery
{
    public const char Or = '|';
    public const char And = ',';

    internal const int Limit = 100;
    internal const string RequiredAttributes = "multiverseId,imageUrl";

    private readonly ICardService _cardService;
    private readonly MtgApiFlipQuery _flipQuery;
    private readonly PageSize _pageSize;
    private readonly LoadingProgress _loadProgress;

    public MtgApiQuery(
        ICardService cardService,
        MtgApiFlipQuery flipQuery,
        PageSize pageSize,
        LoadingProgress loadProgress)
    {
        _cardService = cardService;
        _flipQuery = flipQuery;
        _pageSize = pageSize;
        _loadProgress = loadProgress;
    }

    public IMtgCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
    {
        var query = new MtgCardSearch(_cardService, _flipQuery, _pageSize);
        return query.Where(predicate);
    }

    public IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds)
        => BulkSearchAsync(multiverseIds);

    private async IAsyncEnumerable<Card> BulkSearchAsync(
        IEnumerable<string> multiverseIds,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        const int chunkSize = (int)(Limit * 0.9f); // leave wiggle room for result

        cancel.ThrowIfCancellationRequested();

        var chunks = multiverseIds
            .Distinct()
            .Chunk(chunkSize)
            .ToList();

        _loadProgress.Ticks += chunks.Count;

        foreach (string[] multiverseChunk in chunks)
        {
            if (!multiverseChunk.Any())
            {
                continue;
            }

            string multiverseArgs = string.Join(Or, multiverseChunk);

            var response = await _cardService
                .Where(c => c.MultiverseId, multiverseArgs)
                .AllAsync();

            cancel.ThrowIfCancellationRequested();

            var validated = await _flipQuery.GetCardsAsync(response, cancel);

            foreach (var card in validated)
            {
                yield return card;
            }

            _loadProgress.AddProgress();
        }
    }

    public async Task<Card?> FindAsync(string id, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        cancel.ThrowIfCancellationRequested();

        var result = await _cardService.FindAsync(id);

        cancel.ThrowIfCancellationRequested();

        return await _flipQuery.GetCardAsync(result, cancel);
    }

}
