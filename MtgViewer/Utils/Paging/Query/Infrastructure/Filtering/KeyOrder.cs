using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal readonly record struct KeyOrder(MemberExpression? Key, Ordering Ordering);
