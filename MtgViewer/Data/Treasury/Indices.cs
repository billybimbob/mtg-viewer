using System;

namespace MtgViewer.Data.Treasury;

internal readonly record struct LocationIndex(int Id, string? Name, int? Capacity)
{
    public static explicit operator LocationIndex(StorageSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);

        return new LocationIndex(space.Id, space.Name, space.Capacity);
    }

    public static explicit operator LocationIndex(Location location)
    {
        ArgumentNullException.ThrowIfNull(location);

        return new LocationIndex(
            location.Id, location.Name, (location as Box)?.Capacity);
    }
}

internal readonly record struct HoldIndex(string CardId, LocationIndex Location)
{
    public static explicit operator HoldIndex(Hold hold)
    {
        ArgumentNullException.ThrowIfNull(hold);

        return new HoldIndex(hold.CardId, (LocationIndex)hold.Location);
    }
}

internal readonly record struct ChangeIndex(string CardId, LocationIndex To, LocationIndex? From)
{
    public static explicit operator ChangeIndex(Change change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new ChangeIndex(
            change.CardId,
            (LocationIndex)change.To,
            change.From is null ? null : (LocationIndex)change.From);
    }
}
