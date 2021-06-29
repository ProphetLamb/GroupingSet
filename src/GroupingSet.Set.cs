using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

using KeyValueCollection.Grouping;
using KeyValueCollection.Utility;

namespace KeyValueCollection
{
    public partial class GroupingSet<TKey, TElement>
    {
        /// <summary>
        /// Checks if equality comparers are equal. This is used for algorithms that can
        /// speed up if it knows the other item has unique elements. I.e. if they're using
        /// different equality comparers, then uniqueness assumption between sets break.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool EqualityComparersAreEqual(GroupingSet<TKey, TElement> other) => Comparer.Equals(other.Comparer);

        /// <summary>
        /// Checks if this contains of other's elements. Iterates over other's elements and
        /// returns false as soon as it finds an element in other that's not in this.
        /// Used by SupersetOf, ProperSupersetOf, and SetEquals.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override bool ContainsAllElements(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            foreach (IGrouping<TKey, TElement> element in other)
            {
                if (ContainsKey(element.Key))
                    continue;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines counts that can be used to determine equality, subset, and superset. This
        /// is only used when other is an IEnumerable and not a HashSet. If other is a HashSet
        /// these properties can be checked faster without use of marking because we can assume
        /// other has no duplicates.
        ///
        /// The following count checks are performed by callers:
        /// 1. Equals: checks if unfoundCount = 0 and uniqueFoundCount = m_count; i.e. everything
        /// in other is in this and everything in this is in other
        /// 2. Subset: checks if unfoundCount >= 0 and uniqueFoundCount = m_count; i.e. other may
        /// have elements not in this and everything in this is in other
        /// 3. Proper subset: checks if unfoundCount > 0 and uniqueFoundCount = m_count; i.e
        /// other must have at least one element not in this and everything in this is in other
        /// 4. Proper superset: checks if unfound count = 0 and uniqueFoundCount strictly less
        /// than m_count; i.e. everything in other was in this and this had at least one element
        /// not contained in other.
        ///
        /// An earlier implementation used delegates to perform these checks rather than returning
        /// an ElementCount struct; however this was changed due to the perf overhead of delegates.
        /// </summary>
        /// <param name="other"></param>
        /// <param name="returnIfUnfound">Allows us to finish faster for equals and proper superset
        /// because unfoundCount must be 0.</param>
        protected override unsafe (int, int) CheckUniqueAndUnfoundElements(IEnumerable<IGrouping<TKey, TElement>> other, bool returnIfUnfound)
        {
            // Need special case in case this has no elements.
            if (m_count == 0)
            {
                int numElementsInOther = 0;
                if (other.Any())
                {
                    numElementsInOther++;
                }

                return (0, numElementsInOther);
            }

            Debug.Assert(_buckets != null && m_count > 0, "_buckets was null but count greater than 0");

            int originalCount = m_count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> span = stackalloc int[StackAllocThreshold];
            BitHelper bitHelper = intArrayLength <= StackAllocThreshold ? new BitHelper(span.Slice(0, intArrayLength), clear: true) : new BitHelper(new int[intArrayLength], clear: false);

            int unfoundCount = 0; // count of items in other not found in this
            int uniqueFoundCount = 0; // count of unique items in other found in this

            foreach (IGrouping<TKey, TElement> grouping in other)
            {
                int location = FindItemIndex(grouping.Key, out _);
                if (location >= 0)
                {
                    if (!bitHelper.IsMarked(location))
                    {
                        // Item hasn't been seen yet.
                        bitHelper.MarkBit(location);
                        uniqueFoundCount++;
                    }
                }
                else
                {
                    unfoundCount++;
                    if (returnIfUnfound)
                        break;
                }
            }

            return (uniqueFoundCount, unfoundCount);
        }

        /// <summary>
        /// if other is a set, we can assume it doesn't have duplicate elements, so use this
        /// technique: if can't remove, then it wasn't present in this set, so add.
        ///
        /// As with other methods, callers take care of ensuring that other is a hashset using the
        /// same equality comparer.
        /// </summary>
        /// <param name="other"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override void SymmetricExceptWithUniqueHashSet(GroupingSet<TKey, TElement> other)
        {
            foreach (IGrouping<TKey, TElement> grouping in other)
            {
                TKey key = grouping.Key;
                if (Remove(key))
                    Add(key, grouping);
            }
        }

        /// <summary>
        /// Implementation notes:
        ///
        /// Used for symmetric except when other isn't a HashSet. This is more tedious because
        /// other may contain duplicates. HashSet technique could fail in these situations:
        /// 1. Other has a duplicate that's not in this: HashSet technique would add then
        /// remove it.
        /// 2. Other has a duplicate that's in this: HashSet technique would remove then add it
        /// back.
        /// In general, its presence would be toggled each time it appears in other.
        ///
        /// This technique uses bit marking to indicate whether to add/remove the item. If already
        /// present in collection, it will get marked for deletion. If added from other, it will
        /// get marked as something not to remove.
        ///
        /// </summary>
        /// <param name="other"></param>
        protected override unsafe void SymmetricExceptWithEnumerable(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            int originalCount = m_count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> itemsToRemoveSpan = stackalloc int[StackAllocThreshold / 2];
            BitHelper itemsToRemove = intArrayLength <= StackAllocThreshold / 2 ? new BitHelper(itemsToRemoveSpan.Slice(0, intArrayLength), clear: true) : new BitHelper(new int[intArrayLength], clear: false);

            Span<int> itemsAddedFromOtherSpan = stackalloc int[StackAllocThreshold / 2];
            BitHelper itemsAddedFromOther = intArrayLength <= StackAllocThreshold / 2 ? new BitHelper(itemsAddedFromOtherSpan.Slice(0, intArrayLength), clear: true) : new BitHelper(new int[intArrayLength], clear: false);

            foreach (IGrouping<TKey, TElement> grouping in other)
            {
                if (CreateIfNotPresent(grouping.Key, out int location))
                {
                    _entries![location].AddRange(grouping);
                    // wasn't already present in collection; flag it as something not to remove
                    // *NOTE* if location is out of range, we should ignore. BitHelper will
                    // detect that it's out of bounds and not try to mark it. But it's
                    // expected that location could be out of bounds because adding the item
                    // will increase _lastIndex as soon as all the free spots are filled.
                    itemsAddedFromOther.MarkBit(location);
                }
                else
                {
                    // already there...if not added from other, mark for remove.
                    // *NOTE* Even though BitHelper will check that location is in range, we want
                    // to check here. There's no point in checking items beyond originalCount
                    // because they could not have been in the original collection
                    if (location < originalCount && !itemsAddedFromOther.IsMarked(location))
                        itemsToRemove.MarkBit(location);
                }
            }

            ValueGrouping<TKey, TElement>[]? entries = _entries;
            // if anything marked, remove it
            for (int i = 0; i < originalCount; i++)
            {
                if (itemsToRemove.IsMarked(i))
                {
                    ref ValueGrouping<TKey, TElement> entry = ref entries![i];
                    Remove(entry.Key);
                }
            }
        }

        /// <summary>
        /// If other is a hashset that uses same equality comparer, intersect is much faster
        /// because we can use other's Contains
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override void IntersectWithHashSetWithSameComparer(GroupingSet<TKey, TElement> other)
        {
            ValueGrouping<TKey, TElement>[]? entries = _entries;
            for (int i = 0; i < m_count; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    if (!other.ContainsKey(entry.Key))
                        Remove(entry.Key);
                }
            }
        }

        /// <summary>
        /// Iterate over other. If contained in this, mark an element in bit array corresponding to
        /// its position in _slots. If anything is unmarked (in bit array), remove it.
        ///
        /// This attempts to allocate on the stack, if below StackAllocThreshold.
        /// </summary>
        protected override unsafe void IntersectWithEnumerable(IEnumerable<IGrouping<TKey, TElement>> other)
        {
            Debug.Assert(_buckets != null, "_buckets shouldn't be null; callers should check first");

            // Keep track of current last index; don't want to move past the end of our bit array
            // (could happen if another thread is modifying the collection).
            int originalCount = m_count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> span = stackalloc int[StackAllocThreshold];
            BitHelper bitHelper = intArrayLength <= StackAllocThreshold ? new BitHelper(span.Slice(0, intArrayLength), clear: true) : new BitHelper(new int[intArrayLength], clear: false);

            // Mark if contains: find index of in slots array and mark corresponding element in bit array.
            foreach (IGrouping<TKey, TElement> item in other)
            {
                int location = FindItemIndex(item.Key, out _);
                if (location >= 0)
                    bitHelper.MarkBit(location);
            }

            ValueGrouping<TKey, TElement>[]? entries = _entries;
            // If anything unmarked, remove it. Perf can be optimized here if BitHelper had a
            // FindFirstUnmarked method.
            for (int i = 0; i < originalCount; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries![i];
                if (entry.Next >= -1 && !bitHelper.IsMarked(i))
                    Remove(entry.Key);
            }
        }

        /// <summary>
        /// Implementation Notes:
        /// If other is a hashset and is using same equality comparer, then checking subset is
        /// faster. Simply check that each element in this is in other.
        ///
        /// Note: if other doesn't use same equality comparer, then Contains check is invalid,
        /// which is why callers must take are of this.
        ///
        /// If callers are concerned about whether this is a proper subset, they take care of that.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool IsSubsetOfHashSetWithSameComparer(GroupingSet<TKey, TElement> other)
        {
            foreach (IGrouping<TKey, TElement> item in this)
            {
                if (other.ContainsKey(item.Key))
                    continue;
                return false;
            }

            return true;
        }
    }
}
