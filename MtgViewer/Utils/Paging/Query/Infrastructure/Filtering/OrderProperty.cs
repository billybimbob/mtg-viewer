using System.Linq.Expressions;

namespace EntityFrameworkCore.Paging.Query.Infrastructure.Filtering;

internal readonly record struct OrderProperty(MemberExpression? Member, Ordering Ordering, NullOrder NullOrder);
