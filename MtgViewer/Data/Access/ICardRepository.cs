using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;

namespace MtgViewer.Data.Access;

public interface ICardRepository
{
    Task<IReadOnlyList<string>> GetShuffleOrderAsync(CancellationToken cancellation);

    Task<IReadOnlyCollection<string>> GetExistingCardIdsAsync(IReadOnlyCollection<Card> cards, CancellationToken cancellation);

    Task<IReadOnlyList<CardImage>> GetCardImagesAsync(IReadOnlyList<string> cardIds, CancellationToken cancellation);

    Task<SeekResponse<CardCopy>> GetCardCopiesAsync(CollectionFilter collectionFilter, CancellationToken cancellation);

    Task AddCardsAsync(IReadOnlyCollection<CardRequest> cardRequests, CancellationToken cancellation);
}
