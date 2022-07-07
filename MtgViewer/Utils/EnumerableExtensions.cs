using System.Collections.Generic;

namespace System.Linq;

public static class EnumerableExtensions
{
    public static string Join<T>(this IEnumerable<T> values, char separator)
    {
        ArgumentNullException.ThrowIfNull(values);

        return string.Join(separator, values);
    }

    public static string Join<T>(this IEnumerable<T> values, string? separator = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        return string.Join(separator, values);
    }
}
