using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

public readonly record struct KeyOrder(MemberExpression? Key, Ordering Ordering);
