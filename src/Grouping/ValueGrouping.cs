using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

using KeyValueCollection.DebugViews;
using KeyValueCollection.Utility;

namespace KeyValueCollection.Grouping
{
    [DebuggerDisplay("Key: {Key}, Count: {Count}")]
    [DebuggerTypeProxy(typeof(IGroupingDebugView<,>))]
    public struct ValueGrouping<TKey, TElement> :
        IGrouping<TKey, TElement>,
        IList<TElement>,
        IReadOnlyList<TElement>
        where TKey : notnull
    {
#region Fields

        internal TKey KeyValue;
        internal int HashCode;
        /// <summary>
        /// 0-based index of next entry in chain: -1 means end of chain
        /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
        /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
        /// </summary>
        internal int Next;
        internal TElement[]? Elements;
        private int _count;
        
#endregion

#region Ctors

        internal ValueGrouping(TKey key, int hashCode)
        {
            KeyValue = key;
            HashCode = hashCode;
            Next = 0;
            Elements = null;
            _count = 0;
        }

        internal ValueGrouping(TKey key, int hashCode, IEnumerable<TElement> elements)
            : this(key, hashCode)
        {
            AddRange(elements);
        }

        internal ValueGrouping(TKey key, int hashCode, ReadOnlySpan<TElement> elements)
            : this(key, hashCode)
            {
                    AddRange(elements);
        }

        internal ValueGrouping(ValueGrouping<TKey, TElement> source)
        {
            KeyValue = source.Key;
            HashCode = source.HashCode;
            Next = 0;
            Elements = Array.Empty<TElement>();
            _count = 0;
            
            Grow(source._count);
            source.CopyTo(Elements!, 0);
        }
        
#endregion

#region Properties
        
        public TKey Key => KeyValue;

        public int Count => _count;

        /// <inheritdoc />
        bool ICollection<TElement>.IsReadOnly => false;

        /// <inheritdoc cref="IList{T}.this" />
        public TElement this[int index]
        {
            get => Elements![index];
            set => Elements![index] = value;
        }

        internal Span<TElement> ElementsSpan => Elements != null ? Elements.AsSpan(0, _count) : Span<TElement>.Empty;

#endregion

#region Public members
        
        /// <inheritdoc />
        public void Clear()
        {
            if (Elements != null)
            {
                Array.Clear(Elements, 0, _count);
                _count = 0;
            }
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(TElement item) => IndexOf(item, EqualityComparer<TElement>.Default);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(TElement item, IEqualityComparer<TElement> comparer)
        {
            if (Elements != null)
            {
                for (int i = 0; i < Elements.Length; i++)
                {
                    if (i >= _count) // Skip bound checks by looping over all elements
                        return -1;
                    if (comparer.Equals(Elements[i], item))
                        return i;
                }
            }

            return -1;
        }

        public int IndexOfLast(TElement item) => IndexOfLast(item, EqualityComparer<TElement>.Default);
        
        public int IndexOfLast(TElement item, IEqualityComparer<TElement> comparer)
        {
            if (Elements != null)
            {
                for (int i = Elements.Length - 1; i >= 0; i--)
                {
                    if (i >= _count) // Skip bound checks by looping over all elements
                        return -1;
                    if (comparer.Equals( Elements[i], item))
                        return i;
                }
            }

            return -1;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TElement item) => Contains(item, EqualityComparer<TElement>.Default);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TElement item, IEqualityComparer<TElement> comparer) => IndexOf(item, comparer) >= 0;

        public bool ContainsAll(IEnumerable<TElement> items, IEqualityComparer<TElement> comparer)
        {
            if (Elements != null)
            {
                foreach (TElement element in items)
                {
                    if (Contains(element, comparer))
                        continue;
                    return false;
                }

                return true;
            }

            return false;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(TElement item)
        {
            if (Elements == null || _count >= Elements.Length - 1)
                Grow(1);
            Elements![_count] = item;
            _count++;
        }

        public int AddRange(IEnumerable<TElement> items)
        {
            int count = _count;
            int added;
            switch (items)
            {
                case ICollection<TElement> collection:
                    added = collection.Count;
                    if (Elements == null || added >= Elements.Length - count)
                        Grow(added);
                    collection.CopyTo(Elements!, count);
                    _count += added;
                    return added;
                case IReadOnlyCollection<TElement> sequence:
                    added = sequence.Count;
                    if (Elements == null || added >= Elements.Length - count)
                        Grow(added);
                    goto default;
                default:
                    added = 0;
            foreach (TElement element in items)
            {
                Add(element);
                added++;
            }
                    return added;
            }
        }

        public int AddRange(ReadOnlySpan<TElement> span)
        {
            int added = span.Length;
            if (added != 0)
            {
             
                int count = _count;
                if (Elements == null || added >= Elements.Length - count)
                    Grow(added);
                span.CopyTo(Elements!.AsSpan(count));
                _count += added;   
            }
            return added;
        }

        public void Insert(int index, TElement item)
        {
            if (Elements == null || _count > Elements.Length - 1)
                Grow(1);
            int remaining = _count - index;
            Elements.AsSpan(index, remaining).CopyTo(Elements.AsSpan(index + 1));
            Elements![index] = item;
            _count++;
        }
        
        public void Insert(int index, ReadOnlySpan<TElement> items)
        {
            if (Elements == null || _count > Elements.Length - items.Length)
                Grow(items.Length);
            int remaining = _count - index;
            Elements.AsSpan(index, remaining).CopyTo(Elements.AsSpan(index + items.Length));
            items.CopyTo(Elements.AsSpan(index, items.Length));
            _count += items.Length;
        }

        /// <inheritdoc />
        public bool Remove(TElement item) => Remove(item, EqualityComparer<TElement>.Default);

        /// <inheritdoc cref="ICollection{T}.Remove(T)" />
        public bool Remove(TElement item, IEqualityComparer<TElement> comparer)
        {
            int index = IndexOf(item, comparer);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }

            return false;
        }

        /// <summary>Removes the first occurrences of all <paramref name="items"/> from the grouping.</summary>
        /// <param name="items">The items to remove</param>
        /// <returns>The number of elements removed.</returns>
        public int RemoveAll(IEnumerable<TElement> items) => RemoveAll(items, EqualityComparer<TElement>.Default);

        /// <summary>Removes the first occurrences of all <paramref name="items"/> from the grouping.</summary>
        /// <param name="items">The items to remove</param>
        /// <param name="comparer">The comparer used to determine whether two elements are equal.</param>
        /// <returns>The number of elements removed.</returns>
        public int RemoveAll(IEnumerable<TElement> items, IEqualityComparer<TElement> comparer)
        {
            int removed = 0;

            foreach (TElement element in items)
            {
                if (Remove(element, comparer))
                    removed++;
            }

            return removed;
        }

        public void RemoveAt(int index)
        {
            int count = _count;
            if (index == count - 1)
            {
                _count--;
                return;
            }

            int next = index + 1;
            TElement[] tempPool = ArrayPool<TElement>.Shared.Rent(count - next);
            Span<TElement> rightPart = tempPool.AsSpan(count - next);
            
            Elements.AsSpan(next).CopyTo(rightPart);
            rightPart.CopyTo(Elements.AsSpan(index));
            
            ArrayPool<TElement>.Shared.Return(tempPool);
        }
        
        public void CopyTo(TElement[] array, int arrayIndex)
        {
            if (Elements != null)
                Array.Copy(Elements, 0, array, arrayIndex, _count);
        }

        /// <inheritdoc />
        public IEnumerator<TElement> GetEnumerator() => new ArraySegmentEnumerator<TElement>(Elements, 0, _count);

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => new ArraySegmentEnumerator<TElement>(Elements, 0, _count);

        public int Resize()
        {
            int size = Elements != null ? Elements.Length : 0;
            
            if (_count == 0 && size != 0)
            {
                Elements = null;
                return 0;
            }
            
            if (_count < size && size > 4)
            {
                Array.Resize(ref Elements, _count);
            }

            return size;
        }

        public static ValueGrouping<TKey, TElement> From(IGrouping<TKey, TElement> grouping) => new(grouping.Key, grouping.Key.GetHashCode(), grouping);

        public static ValueGrouping<TKey, TElement> From(IGrouping<TKey, TElement> grouping, IEqualityComparer<TKey> hashCreator) => new(grouping.Key, hashCreator.GetHashCode(grouping.Key), grouping);

#endregion

#region Internal members
        
        private void Grow(int additionalCapacityBeyondCount)
        {
            if (Elements != null)
            {
                Debug.Assert(additionalCapacityBeyondCount > 0, "additionalCapacityBeyondCount > 0");
                Debug.Assert(_count > Elements.Length - additionalCapacityBeyondCount, "_count > Elements.Length - additionalCapacityBeyondCount");

                TElement[] array = new TElement[(int)Math.Max((uint)(_count + additionalCapacityBeyondCount), (uint)(Elements.Length * 2))];

                if (Elements.Length != 0)
                {
                    Elements.AsSpan(0, _count).CopyTo(array);
                    Elements = array;
                }
                else
                {
                    Elements = array;
                }
            }
            else
            {
                Debug.Assert(_count == 0, "_count == 0");
                Elements = new TElement[Math.Max(additionalCapacityBeyondCount, 4)];
            }
        }

        internal ArraySegment<TElement> GetSegment() => Elements != null ? new(Elements, 0, _count) : ArraySegment<TElement>.Empty;

#endregion
    }
}
