using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace MTGViewer.Utils;

internal static partial class PersistentComponentStateExtensions
{
    public static TData? GetValueOrDefault<TData>(this PersistentComponentState persistent, string key)
    {
        if (persistent.TryTakeFromJson<TData>(key, out var data))
        {
            return data;
        }

        return default;
    }

    public static bool TryGetData<TData>(
        this PersistentComponentState persistent,
        string key,
        [NotNullWhen(true)] out TData? data)
    {
        if (persistent.TryTakeFromJson(key, out data) && data is not null)
        {
            return true;
        }

        return false;
    }
}
