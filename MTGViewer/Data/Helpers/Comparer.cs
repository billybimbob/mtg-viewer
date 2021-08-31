using System;
using System.Collections.Generic;


namespace MTGViewer.Data
{
    public class EntityComparer<E> : IEqualityComparer<E>
    {
        private Func<E, object> _property;

        public EntityComparer(Func<E, object> property)
        {
            _property = property;
        }

        public bool Equals(E a, E b)
        {
            if (object.ReferenceEquals(a, b))
            {
                return true;
            }
            else if (a is null || b is null)
            {
                return false;
            }

            return _property(a).Equals(_property(b));
        }

        public int GetHashCode(E entity)
        {
            return entity is null ? 0 : _property(entity).GetHashCode();
        }
    }

}