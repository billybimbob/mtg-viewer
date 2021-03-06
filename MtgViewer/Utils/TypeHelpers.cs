using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace MtgViewer.Utils;

internal static class TypeHelpers
{
    private static bool IsEntityType(Type type)
        => type is { IsValueType: false, IsGenericType: false };

    internal static PropertyInfo GetKeyProperty(Type entityType)
    {
        if (!IsEntityType(entityType))
        {
            throw new ArgumentException("The type is a value or generic type", nameof(entityType));
        }

        const string id = "Id";

        string typeId = $"{entityType.Name}{id}";

        // could memo this in a static dict?
        PropertyInfo? key;

        var entityProperties = entityType.GetTypeInfo().GetProperties();

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
}
