using System;
using System.Collections.Generic;


namespace MTGViewer.Data
{
    internal class EntityComparer<E> : IEqualityComparer<E>
    {
        private Func<E, object> _property;

        internal EntityComparer(Func<E, object> property)
        {
            _property = property;
        }

        public bool Equals(E a, E b)
        {
            if (object.ReferenceEquals(a, b))
            {
                return true;
            }
            else if (a == null || b == null)
            {
                return false;
            }

            return _property(a).Equals(_property(b));
        }

        public int GetHashCode(E entity)
        {
            return entity == null ? 0 : _property(entity).GetHashCode();
        }
    }

}