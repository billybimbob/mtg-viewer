using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.EntityFrameworkCore.Query;

namespace System.Paging.Query;

public static class ExpressionHelpers
{
    public static bool IsOrderBy(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name 
            is nameof(Queryable.OrderBy)
                or nameof(Queryable.OrderByDescending);
    }


    public static bool IsOrderedMethod(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name 
            is nameof(Queryable.OrderBy)
                or nameof(Queryable.ThenBy)
                or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenByDescending);
    }


    public static bool IsDescending(MethodCallExpression orderBy)
    {
        return orderBy.Method.Name
            is nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenByDescending);
    }



    public static IEnumerable<MemberExpression> GetLineage(MemberExpression? member)
    {
        while (member is not null)
        {
            yield return member;
            member = member.Expression as MemberExpression;
        }
    }


    public static bool IsDescendant(MemberExpression? node, MemberExpression possibleAncestor)
    {
        var equalExpression = ExpressionEqualityComparer.Instance;

        return GetLineage(node)
            .Any(m => equalExpression.Equals(m, possibleAncestor));
    }


    public static bool IsDescendant(MemberExpression? node, ConstantExpression possibleAncestor)
    {
        var root = node?.Expression;

        while (root is MemberExpression member)
        {
            root = member.Expression;
        }

        return root is ParameterExpression p
            && p.Type == possibleAncestor.Type;
    }


    public static PropertyInfo GetKeyProperty<TEntity>() where TEntity : class
    {
        return GetKeyProperty(typeof(TEntity));
    }


    public static PropertyInfo GetKeyProperty(Type entityType)
    {
        if (!IsEntityType(entityType))
        {
            throw new ArgumentException(nameof(entityType));
        }

        const string id = "Id";
        const BindingFlags binds = BindingFlags.Instance | BindingFlags.Public;

        string typeId = $"{entityType.Name}{id}";

        // could memo this in a static dict?
        PropertyInfo? key;

        var entityProperties = entityType.GetProperties(binds);

        key = entityProperties
            .FirstOrDefault(e => e.GetCustomAttribute<KeyAttribute>() is not null);

        if (key != default)
        {
            return key;
        }

        key = entityProperties
            .FirstOrDefault(e => e.Name == id || e.Name == typeId);

        if (key != default)
        {
            return key;
        }

        key = entityProperties.FirstOrDefault(e => e.Name.Contains(id));

        if (key != default)
        {
            return key;
        }

        var nestedType = entityProperties
            .Select(e => e.PropertyType)
            .FirstOrDefault(IsEntityType);

        if (nestedType != default)
        {
            return GetKeyProperty(nestedType);
        }

        throw new ArgumentException($"type {entityType.Name} is invalid");
    }

    
    private static bool IsEntityType(Type type)
    {
        return !type.IsValueType && !type.IsGenericType;
    }
}