using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MTGViewer.Data.Internal
{
    internal class PropertyComparer<E> : IEqualityComparer<E>
    {
        private IEnumerable<Func<E, object>> _properties;

        public PropertyComparer(Func<E, object> property, params Func<E, object>[] thenProperties)
        {
            _properties = thenProperties
                .Prepend(property)
                .ToList();
        }

        public bool Equals(E? a, E? b)
        {
            if (object.ReferenceEquals(a, b))
            {
                return true;
            }
            else if (a is null || b is null)
            {
                return false;
            }

            return _properties.All(property =>
            {
                var aProp = property(a);
                var bProp = property(b);

                return aProp is null && bProp is null
                    || (aProp?.Equals(bProp) ?? false);
            });
        }

        public int GetHashCode(E entity)
        {
            return entity is null 
                ? 0 
                : _properties.Aggregate(0, (hash, property) =>
                    hash ^ (property(entity)?.GetHashCode() ?? 0));
        }
    }


    public class CardNameComparer : IComparer<Card>
    {
        public int Compare(Card? cardA, Card? cardB)
        {
            var nameCompare = string.Compare(
                cardA?.Name, 
                cardB?.Name, 
                StringComparison.InvariantCulture);

            if (nameCompare != 0)
            {
                return nameCompare;
            }

            return string.Compare(
                cardA?.SetName,
                cardB?.SetName,
                StringComparison.InvariantCulture);
        }
    }
}