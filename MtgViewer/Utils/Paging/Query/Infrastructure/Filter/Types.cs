using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filter;

internal readonly record struct KeyOrder(MemberExpression? Key, Ordering Ordering);

internal enum Ordering
{
    Ascending,
    Descending,
}

internal enum NullOrder
{
    None,
    Before,
    After
}
