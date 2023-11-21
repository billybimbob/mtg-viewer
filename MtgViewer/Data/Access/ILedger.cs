using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using MtgViewer.Data.Projections;

namespace MtgViewer.Data.Access;

public interface ILedger
{
    Task<IReadOnlyList<RecentTransaction>> GetRecentChangesAsync(int size, CancellationToken cancellationToken);
}
