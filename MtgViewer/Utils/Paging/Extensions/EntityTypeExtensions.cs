using System;
using System.Linq;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace EntityFrameworkCore.Paging.Extensions;

internal static class EntityTypeExtensions
{
    internal static PropertyInfo GetKeyInfo(this IEntityType entity)
    {
        var entityId = entity.FindPrimaryKey();

        if (entityId is not { Properties.Count: 1, Properties: var properties }
            || properties[0].PropertyInfo is not PropertyInfo getKey)
        {
            throw new NotSupportedException("Only single primary keys are supported");
        }

        return getKey;
    }

    internal static PropertyInfo GetKeyInfo<TKey>(this IEntityType entity)
    {
        var entityId = entity.FindPrimaryKey();

        if (typeof(TKey) != entityId?.GetKeyType())
        {
            throw new ArgumentException($"{typeof(TKey).Name} is the not correct key type");
        }

        if (entityId is not { Properties.Count: 1, Properties: var properties }
            || properties[0].PropertyInfo is not PropertyInfo getKey)
        {
            throw new NotSupportedException("Only single primary keys are supported");
        }

        return getKey;
    }

    internal static IEntityType GetEntityType<T>(this QueryRootExpression root)
    {
        if (root.EntityType.ClrType == typeof(T))
        {
            return root.EntityType;
        }

        var navEntity = root.EntityType
            .GetNavigations()
            .Where(n => n.ClrType == typeof(T))
            .Select(n => n.TargetEntityType)
            .FirstOrDefault();

        if (navEntity is null)
        {
            throw new InvalidOperationException($"Type {typeof(T).Name} could not be found");
        }

        return navEntity;
    }
}
