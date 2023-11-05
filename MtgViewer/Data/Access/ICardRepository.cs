using System.Threading;
using System.Threading.Tasks;

using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;

namespace MtgViewer.Data.Access;

public interface ICardRepository
{
    Task<SeekResponse<CardCopy>> GetCardsAsync(CollectionFilter collectionFilter, CancellationToken cancellation);
}
