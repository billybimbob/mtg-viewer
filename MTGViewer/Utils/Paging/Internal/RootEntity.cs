using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

internal class FindRootQuery : ExpressionVisitor
{
    private static FindRootQuery? s_instance;
    public static ExpressionVisitor Instance => s_instance ??= new();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Arguments.ElementAtOrDefault(0) is not Expression parent)
        {
            return node;
        }

        if (parent is QueryRootExpression root)
        {
            return root;
        }

        return Visit(parent);
    }
}

internal static class EntityTypeExtensions
{
    internal static PropertyInfo GetKeyInfo<TKey>(this IEntityType entity)
    {
        var entityId = entity.FindPrimaryKey();

        if (typeof(TKey) != entityId?.GetKeyType())
        {
            throw new ArgumentException($"{typeof(TKey).Name} is the not correct key type");
        }

        if (entityId is not { Properties.Count: 1, Properties: IReadOnlyList<IProperty> properties }
            || properties[0].PropertyInfo is not PropertyInfo getKey)
        {
            throw new NotSupportedException("Only single primary keys are supported");
        }

        return getKey;
    }
}
