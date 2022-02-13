using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace System.Paging;


internal class PageBuilder<T>
{
    internal PageBuilder(IQueryable<T> source, int? index, int pageSize)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (pageSize < 0)
        {
            throw new ArgumentException(nameof(pageSize));
        }

        if (index < 0)
        {
            throw new ArgumentException(nameof(index));
        }

        Source = source;
        PageIndex = index ?? 0;
        PageSize = pageSize;
    }


    private PageBuilder(PageBuilder<T> copy)
    {
        Source = copy.Source;
        PageIndex = copy.PageIndex;
        PageSize = copy.PageSize;
    }


    public IQueryable<T> Source { get; }

    public int PageIndex { get; }

    public int PageSize { get; }

    public SeekDirection Direction { get; init; }

    public T? Origin { get; init; }


    public PageBuilder<T> After(T origin)
    {
        return new PageBuilder<T>(this)
        {
            Origin = origin,
            Direction = SeekDirection.Forward
        };
    }

    public PageBuilder<T> Before(T origin)
    {
        return new PageBuilder<T>(this)
        {
            Origin = origin,
            Direction = SeekDirection.Backwards
        };
    }


    public IQueryable<T> CreateQuery()
    {
        if (Origin is null)
        {
            return Source
                .Skip(PageIndex * PageSize)
                .Take(PageSize);
        }

        var seekCondition = KeyFilter.BuildOriginFilter(this);

        if (Direction is SeekDirection.Forward)
        {
            return Source
                .Where(seekCondition)
                .Take(PageSize);
        }
        else
        {
            return Source
                .Reverse()
                .Where(seekCondition)
                .Take(PageSize);
        }
    }


    public OffsetList<T> ToOffsetList()
    {
        int totalItems = Source.Count();

        var items = Source
            .Skip(PageIndex * PageSize)
            .Take(PageSize)
            .ToList();

        var offset = new Offset(PageIndex, totalItems, PageSize);

        return new OffsetList<T>(offset, items);
    }


    public async Task<OffsetList<T>> ToOffsetListAsync(CancellationToken cancellationToken = default)
    {
        int totalItems = await Source
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await Source
            .Skip(PageIndex * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var offset = new Offset(PageIndex, totalItems, PageSize);

        return new OffsetList<T>(offset, items);
    }



    #region Seek List

    public Task<SeekList<T>> ToSeekListAsync(CancellationToken cancellationToken = default)
    {
        if (Origin is null)
        {
            return FirstSeekListAsync(cancellationToken);
        }

        var seekCondition = KeyFilter.BuildOriginFilter(this);

        return Direction switch
        {
            SeekDirection.Backwards => OriginSeekBackListAsync(seekCondition, cancellationToken),
            SeekDirection.Forward or _ => OriginSeekListAsync(seekCondition, cancellationToken)
        };
    }


    private async Task<SeekList<T>> FirstSeekListAsync(CancellationToken cancel)
    {
        var items = await Source
            .Take(PageSize)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        bool hasNext = await Source
            .Skip(PageSize) // offset is constant, so should be fine, keep eye on
            .AnyAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<T>(index: 0, hasNext, items);

        return new SeekList<T>(seek, items);
    }


    private async Task<SeekList<T>> OriginSeekListAsync(
        Expression<Func<T, bool>> seekCondition,
        CancellationToken cancel)
    {
        var items = await Source
            .Where(seekCondition)
            .Take(PageSize)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        bool hasNext = await Source
            .Where(seekCondition)
            .Skip(PageSize) // offset is constant, so should be fine, keep eye on
            .AnyAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<T>(PageIndex, hasNext, items);

        return new SeekList<T>(seek, items);
    }


    private async Task<SeekList<T>> OriginSeekBackListAsync(
        Expression<Func<T, bool>> seekCondition,
        CancellationToken cancel)
    {
        bool hasPrevious = await Source
            .Reverse()
            .Where(seekCondition)
            .Skip(PageSize) // offset is constant, so should be fine, keep eye on
            .AnyAsync(cancel)
            .ConfigureAwait(false);

        if (hasPrevious && PageIndex == 0)
        {
            return await ResetSeekListAsync(cancel).ConfigureAwait(false);
        }

        var items = await Source
            .Reverse()
            .Where(seekCondition)
            .Take(PageSize)
            .Reverse()
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<T>(hasPrevious, PageIndex, items);

        return new SeekList<T>(seek, items);
    }


    private async Task<SeekList<T>> ResetSeekListAsync(CancellationToken cancel)
    {
        var items = await Source
            .Take(PageSize)
            .ToListAsync(cancel)
            .ConfigureAwait(false);

        var seek = new Seek<T>(index: 0, hasNext: true, items);

        return new SeekList<T>(seek, items);
    }

    #endregion
}


internal static partial class PagingExtensions
{
    internal static PageBuilder<TEntity> PageBy<TEntity>(
        this IQueryable<TEntity> source,
        int? index, 
        int pageSize)
    {
        return new PageBuilder<TEntity>(source, index, pageSize);
    }
}