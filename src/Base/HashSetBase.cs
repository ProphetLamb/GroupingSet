using System.Collections;
using System.Collections.Generic;

namespace KeyValueCollection.Base
{
    public abstract class HashSetBase<TItem, TSelf> : 
        ISet<TItem>,
#if NET5_0
        IReadOnlySet<TItem>
#else
        IReadOnlyCollection<TItem>
#endif
        where TSelf : HashSetBase<TItem, TSelf>
    {
        protected int m_count;
        
        public int Count => m_count;

        /// <inheritdoc />
        bool ICollection<TItem>.IsReadOnly => false;
        
        /// <inheritdoc />
        public void UnionWith(IEnumerable<TItem> other)
        {
            foreach (TItem element in other)
                Add(element);
        }
        
        /// <inheritdoc />
        public void ExceptWith(IEnumerable<TItem> other)
        {
            // This is already the empty set; return.
            if (Count == 0)
                return;

            // Special case if other is this; a set minus itself is the empty set.
            if (ReferenceEquals(other, this))
            {
                Clear();
                return;
            }

            // Remove every element in other from this.
            foreach (TItem element in other)
                Remove(element);
        }
        /// <inheritdoc />
        void ICollection<TItem>.Add(TItem items) => Add(items);
        
        /// <inheritdoc />
        public void IntersectWith(IEnumerable<TItem> other)
        {
            // Intersection of anything with empty set is empty set, so return if count is 0.
            // Same if the set intersecting with itself is the same set.
            if (Count == 0 || ReferenceEquals(other, this))
            {
                return;
            }

            // If other is known to be empty, intersection is empty set; remove all elements, and we're done.
            if (other is ICollection<TItem> otherAsCollection)
            {
                if (otherAsCollection.Count == 0)
                {
                    Clear();
                    return;
                }

                // Faster if other is a hashset using same equality comparer; so check
                // that other is a hashset using the same equality comparer.
                if (other is TSelf otherAsSet && EqualityComparersAreEqual((TSelf)this, otherAsSet))
                {
                    IntersectWithHashSetWithSameComparer(otherAsSet);
                    return;
                }
            }

            IntersectWithEnumerable(other);
        }

        /// <inheritdoc cref="ISet{T}.IsProperSubsetOf" />
        public bool IsProperSubsetOf(IEnumerable<TItem> other)
        {
            // No set is a proper subset of itself.
            if (ReferenceEquals(other, this))
            {
                return false;
            }

            if (other is ICollection<TItem> otherAsCollection)
            {
                // No set is a proper subset of an empty set.
                if (otherAsCollection.Count == 0)
                {
                    return false;
                }

                // The empty set is a proper subset of anything but the empty set.
                if (Count == 0)
                {
                    return otherAsCollection.Count > 0;
                }

                // Faster if other is a hashset (and we're using same equality comparer).
                if (other is TSelf otherAsSet && EqualityComparersAreEqual((TSelf)this, otherAsSet))
                {
                    if (Count >= otherAsSet.Count)
                        return false;

                    // This has strictly less than number of items in other, so the following
                    // check suffices for proper subset.
                    return IsSubsetOfHashSetWithSameComparer(otherAsSet);
                }
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: false);
            return uniqueCount == Count && unfoundCount > 0;
        }
        
        /// <inheritdoc cref="ISet{T}.IsProperSupersetOf" />
        public bool IsProperSupersetOf(IEnumerable<TItem> other)
        {
            // The empty set isn't a proper superset of any set, and a set is never a strict superset of itself.
            if (Count == 0 || ReferenceEquals(other, this))
                return false;

            if (other is ICollection<TItem> otherAsCollection)
            {
                // If other is the empty set then this is a superset.
                if (otherAsCollection.Count == 0)
                {
                    // Note that this has at least one element, based on above check.
                    return true;
                }

                // Faster if other is a hashset with the same equality comparer
                if (other is TSelf otherAsSet && EqualityComparersAreEqual((TSelf)this, otherAsSet))
                {
                    if (otherAsSet.Count >= Count)
                        return false;

                    // Now perform element check.
                    return ContainsAllElements(otherAsSet);
                }
            }

            // Couldn't fall out in the above cases; do it the long way
            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: true);
            return uniqueCount < Count && unfoundCount == 0;
        }

        /// <inheritdoc cref="ISet{T}.IsSubsetOf" />
        public bool IsSubsetOf(IEnumerable<TItem> other)
        {
            // The empty set is a subset of any set, and a set is a subset of itself.
            // Set is always a subset of itself
            if (Count == 0 || ReferenceEquals(other, this))
            {
                return true;
            }

            // Faster if other has unique elements according to this equality comparer; so check
            // that other is a hashset using the same equality comparer.
            if (other is TSelf otherAsSet && EqualityComparersAreEqual((TSelf)this, otherAsSet))
            {
                // if this has more elements then it can't be a subset
                if (Count > otherAsSet.Count)
                {
                    return false;
                }

                // already checked that we're using same equality comparer. simply check that
                // each element in this is contained in other.
                return IsSubsetOfHashSetWithSameComparer(otherAsSet);
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: false);
            return uniqueCount == Count && unfoundCount >= 0;
        }

        /// <inheritdoc cref="ISet{T}.IsSupersetOf" />
        public bool IsSupersetOf(IEnumerable<TItem> other)
        {
            // A set is always a superset of itself.
            if (ReferenceEquals(other, this))
            {
                return true;
            }

            // Try to fall out early based on counts.
            if (other is ICollection<TItem> otherAsCollection)
            {
                // If other is the empty set then this is a superset.
                if (otherAsCollection.Count == 0)
                {
                    return true;
                }

                // Try to compare based on counts alone if other is a hashset with same equality comparer.
                if (other is TSelf otherAsSet &&
                    EqualityComparersAreEqual((TSelf)this, otherAsSet) &&
                    otherAsSet.Count > Count)
                {
                    return false;
                }
            }

            return ContainsAllElements(other);
        }

        /// <inheritdoc cref="ISet{T}.Overlaps" />
        public bool Overlaps(IEnumerable<TItem> other)
        {
            if (Count == 0)
            {
                return false;
            }

            // Set overlaps itself
            if (ReferenceEquals(other, this))
            {
                return true;
            }

            foreach (TItem element in other)
            {
                if (Contains(element))
                    return true;
            }

            return false;
        }

        /// <inheritdoc cref="ISet{T}.SetEquals" />
        public bool SetEquals(IEnumerable<TItem> other)
        {
            // A set is equal to itself.
            if (ReferenceEquals(other, this))
                return true;

            // Faster if other is a hashset and we're using same equality comparer.
            if (other is TSelf otherAsSet && EqualityComparersAreEqual((TSelf)this, otherAsSet))
            {
                // Attempt to return early: since both contain unique elements, if they have
                // different counts, then they can't be equal.
                if (Count != otherAsSet.Count)
                {
                    return false;
                }

                // Already confirmed that the sets have the same number of distinct elements, so if
                // one is a superset of the other then they must be equal.
                return ContainsAllElements(otherAsSet);
            }
            else
            {
                // If this count is 0 but other contains at least one element, they can't be equal.
                if (Count == 0 &&
                    other is ICollection<TItem> otherAsCollection &&
                    otherAsCollection.Count > 0)
                {
                    return false;
                }

                (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: true);
                return uniqueCount == Count && unfoundCount == 0;
            }
        }

        /// <inheritdoc />
        public void SymmetricExceptWith(IEnumerable<TItem> other)
        {
            // If set is empty, then symmetric difference is other.
            if (Count == 0)
            {
                UnionWith(other);
                return;
            }

            // Special-case this; the symmetric difference of a set with itself is the empty set.
            if (ReferenceEquals(other, this))
            {
                Clear();
                return;
            }

            // If other is a HashSet, it has unique elements according to its equality comparer,
            // but if they're using different equality comparers, then assumption of uniqueness
            // will fail. So first check if other is a hashset using the same equality comparer;
            // symmetric except is a lot faster and avoids bit array allocations if we can assume
            // uniqueness.
            if (other is TSelf otherAsSet && EqualityComparersAreEqual((TSelf)this, otherAsSet))
            {
                SymmetricExceptWithUniqueHashSet(otherAsSet);
            }
            else
            {
                SymmetricExceptWithEnumerable(other);
            }
        }
        
        /// <inheritdoc cref="ICollection{T}.CopyTo"/>
        public void CopyTo(TItem[] array) => CopyTo(array, 0, Count);

        /// <inheritdoc cref="ICollection{T}.CopyTo"/>
        public void CopyTo(TItem[] array, int arrayIndex) => CopyTo(array, arrayIndex, Count);
        
        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public List<TItem> ToList()
        {
            var list = InternalToList();
            list.TrimExcess();
            return list;
        }

        public TItem[] ToArray() => InternalToList().ToArray();

        protected abstract List<TItem> InternalToList();
        
        /// <inheritdoc />
        public abstract bool Add(TItem items);

        /// <inheritdoc cref="CopyTo(TItem[])" />
        public abstract void CopyTo(TItem[] array, int arrayIndex, int count);

        public abstract bool Remove(TItem element);

        public abstract void Clear();

        public abstract bool Contains(TItem elements);
        
        /// <inheritdoc />
        public abstract IEnumerator<TItem> GetEnumerator();

        protected abstract bool ContainsAllElements(IEnumerable<TItem> otherAsSet);

        protected abstract void IntersectWithEnumerable(IEnumerable<TItem> other);

        protected abstract void IntersectWithHashSetWithSameComparer(TSelf otherAsSet);

        internal abstract bool EqualityComparersAreEqual(TSelf setBase, TSelf otherAsSet);

        protected abstract (int, int) CheckUniqueAndUnfoundElements(IEnumerable<TItem> other, bool returnIfUnfound);

        internal abstract bool IsSubsetOfHashSetWithSameComparer(TSelf otherAsSet);
        
        protected abstract void SymmetricExceptWithEnumerable(IEnumerable<TItem> other);

        protected abstract void SymmetricExceptWithUniqueHashSet(TSelf otherAsSet);
    }
}
