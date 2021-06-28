using System.Collections.Generic;
using System.Linq;

namespace KeyValueCollection.Utility
{
    /// <summary>Equality comparer for hashsets of hashsets</summary>
    internal sealed class GroupingSetEqualityComparer<TKey, TElement> : IEqualityComparer<GroupingSet<TKey, TElement>?>
        where TKey : notnull
    {
        public bool Equals(GroupingSet<TKey, TElement>? left, GroupingSet<TKey, TElement>? right)
        {
            // If they're the exact same instance, they're equal.
            if (ReferenceEquals(left, right))
                return true;

            // They're not both null, so if either is null, they're not equal.
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
                return false;

            EqualityComparer<TKey> defaultKeyComparer = EqualityComparer<TKey>.Default;

            // If both sets use the same comparer, they're equal if they're the same
            // size and one is a "subset" of the other.
            if (left.EqualityComparersAreEqual(left, right))
                return left.Count == right.Count && right.IsSubsetOfHashSetWithSameComparer(left);

            // Otherwise, do an O(N^2) match.
            foreach (IGrouping<TKey, TElement> rightI in right)
            {
                bool found = false;
                foreach (IGrouping<TKey, TElement> leftI in left)
                {
                    if (!defaultKeyComparer.Equals(rightI.Key, leftI.Key))
                        continue;
                    found = true;
                    break;
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(GroupingSet<TKey, TElement>? obj)
        {
            int hashCode = 0; // default to 0 for null/empty set

            if (obj != null)
            {
                foreach (IGrouping<TKey, TElement> t in obj)
                    hashCode ^= t.GetHashCode(); // same hashcode as as default comparer
            }

            return hashCode;
        }

        // Equals method for the comparer itself.
        public override bool Equals(object? obj) => obj is GroupingSetEqualityComparer<TKey, TElement>;

        public override int GetHashCode() => EqualityComparer<TKey>.Default.GetHashCode();
    }
}
