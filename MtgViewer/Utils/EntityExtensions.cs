using System;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace MtgViewer.Utils;

internal static class EntityExtensions
{
    internal static string DisplayName<TEntity, TProperty>(Expression<Func<TEntity, TProperty>> property)
        where TEntity : class
    {
        var memberExpr = property.Body as MemberExpression;

        return DisplayName(memberExpr?.Member);
    }

    internal static string DisplayName<TEntity, TProperty>(
        this TEntity _, Expression<Func<TEntity, TProperty>> property) where TEntity : class
        => DisplayName(property);

    internal static string DisplayName<TEntity>(this TEntity _, string property)
    {
        var member = typeof(TEntity).GetProperty(property);

        return DisplayName(member);
    }

    private static string DisplayName(MemberInfo? member)
    {
        var display = member?.GetCustomAttribute<DisplayAttribute>();

        return display?.GetName() ?? member?.Name ?? string.Empty;
    }
}
