using System;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

#nullable enable

namespace MTGViewer.Data
{
    public static class EntityExtension
    {
        public static string DisplayName<TEntity, TProperty>(
            this TEntity entity, Expression<Func<TEntity, TProperty>> property)
            where TEntity : class
        {
            var memberExpr = property.Body as MemberExpression;

            return DisplayName(memberExpr?.Member);
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
    }
}