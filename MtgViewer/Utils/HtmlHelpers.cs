using System;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace MtgViewer.Utils;

public static class HtmlHelpers
{
    internal static string GetDisplay<TEntity, TProperty>(Expression<Func<TEntity, TProperty>> property)
        where TEntity : class
    {
        var member = (property.Body as MemberExpression)?.Member;

        var display = member?.GetCustomAttribute<DisplayAttribute>();

        return display?.GetName() ?? member?.Name ?? string.Empty;
    }

    public static string GetId<TEntity, TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        if (property.Body is not MemberExpression { Member.Name: string name })
        {
            return string.Empty;
        }

        return $"{typeof(TEntity).Name}-{name}";
    }
}
