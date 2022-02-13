using System;
using System.Linq.Expressions;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

using MTGViewer.Data;

namespace MTGViewer.Services;

public interface IMTGQuery
{
    public const char Or = '|';
    public const char And = ',';

    public int Limit { get; }

    public void Reset();

    public IMTGQuery Where(Expression<Func<CardQuery, bool>> predicate);

    public Task<OffsetList<Card>> SearchAsync(CancellationToken cancel = default);

    public Task<Card?> FindAsync(string id, CancellationToken cancel = default);
}


public static partial class IMTGQueryExtensions
{
    public static IMTGQuery Where(this IMTGQuery mtgQuery, CardQuery cardQuery)
    {
        if (mtgQuery is null)
        {
            throw new ArgumentNullException(nameof(mtgQuery));
        }

        if (cardQuery is null)
        {
            throw new ArgumentNullException(nameof(cardQuery));
        }

        const BindingFlags binds = BindingFlags.Instance | BindingFlags.Public;

        foreach (var info in typeof(CardQuery).GetProperties(binds))
        {
            if (info.CanRead
                && info.CanWrite
                && info.GetValue(cardQuery) is object arg)
            {
                mtgQuery.Where( GetPredicate(info, arg) );
            }
        }

        return mtgQuery;
    }


    private static Expression<Func<CardQuery, bool>> GetPredicate(PropertyInfo propertyInfo, object arg)
    {
        var param = Expression.Parameter(
            typeof(CardQuery),
            typeof(CardQuery).Name[0].ToString().ToLower());

        var equality = Expression.Equal(
            Expression.Property(param, propertyInfo),
            Expression.Constant(arg));

        return Expression.Lambda<Func<CardQuery, bool>>(equality, param);
    }
}