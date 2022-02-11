using System;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MTGViewer.Data.Internal;

public static class EntityExtensions
{
    public static string DisplayName<TEntity, TProperty>(Expression<Func<TEntity, TProperty>> property)
        where TEntity : class
    {
        var memberExpr = property.Body as MemberExpression;

        return DisplayName(memberExpr?.Member);
    }


    public static string DisplayName<TEntity, TProperty>(
        this TEntity entity, Expression<Func<TEntity, TProperty>> property)
        where TEntity : class
    {
        return DisplayName(property);
    }


    public static string DisplayName<TEntity>(this TEntity entity, string property)
    {
        var member = typeof(TEntity).GetProperty(property);

        return DisplayName(member);
    }


    private static string DisplayName(MemberInfo? member)
    {
        var display = member?.GetCustomAttribute<DisplayAttribute>();

        return display?.GetName() ?? member?.Name ?? string.Empty;
    }


    public static void Reset<TEntity>(this TEntity entity)
    {
        const BindingFlags searchProps = BindingFlags.Public | BindingFlags.Instance;
        const string system = "System";

        if (entity == null)
        {
            return;
        }

        foreach (var prop in typeof(TEntity).GetProperties(searchProps))
        {

            if (!prop.GetSetMethod()?.IsPublic ?? true)
            {
                continue;
            }

            var propType = prop.PropertyType;

            if (propType != typeof(string)
                && propType.IsAssignableTo(typeof(IEnumerable)))
            {
                // ignore collection props
                continue;
            }

            if (propType.Namespace != system)
            {
                // ignore reference props that aren't strings
                continue;
            }

            var defaultProp = propType.IsValueType 
                && Nullable.GetUnderlyingType(propType) == null
                ? Activator.CreateInstance(propType)
                : null;

            if (prop.GetValue(entity) == defaultProp)
            {
                continue;
            }

            prop.SetValue(entity, defaultProp);
        }
    }


    public static PropertyInfo GetKeyProperty<TEntity>() where TEntity : class
    {
        const string id = "Id";
        const BindingFlags binds = BindingFlags.Instance | BindingFlags.Public;

        string typeId = $"{typeof(TEntity).Name}{id}";

        // could memo this in a static dict?
        PropertyInfo? key;

        var entityProperties = typeof(TEntity).GetProperties(binds);

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

        throw new ArgumentException($"type {typeof(TEntity).Name} is invalid");
    }
}