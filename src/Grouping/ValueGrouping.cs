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
    [DebuggerDisplay("Key: \"{Key}\", Count: {Count}")]
    [DebuggerTypeProxy(typeof(IGroupingDebugView<,>))]
    public struct ValueGrouping<TKey, TElement> : 
        IGrouping<TKey, TElement>,
        IList<TElement>,
        IReadOnlyList<TElement>
        where TKey : notnull
    {
#region Fields

        internal readonly TKey _key;
        internal int HashCode;
        /// <summary>
        /// 0-based index of next entry in chain: -1 means end of chain
        /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
        /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
        /// </summary>
        internal int Next;
        internal TElement[]? _elements;
        internal int _count;
        
#endregion

#region Ctors

        internal ValueGrouping(TKey key, int hashCode)
        {
            _key = key;
            HashCode = hashCode;
            Next = 0;
            _elements = null;
            _count = 0;
        }

        internal ValueGrouping(TKey key, int hashCode, IEnumerable<TElement> elements)
        {
            _key = key;
            HashCode = hashCode;
            Next = 0;

            switch (elements)
            {
                case ICollection<TElement> collection:
                    _count = collection.Count;
                    _elements = new TElement[_count];
                    collection.CopyTo(_elements, 0);
                    break;
                case IReadOnlyCollection<TElement> sequence:
                    _count = 0;
                    _elements = new TElement[sequence.Count];
                    AddRange(sequence);
                    break;
                default:
                    _count = 0;
                    _elements = null;
                    AddRange(elements);
                    break;
            }
        }

        internal ValueGrouping(ValueGrouping<TKey, TElement> source)
        {
            _key = source._key;
            HashCode = source.HashCode;
            Next = 0;
            _elements = Array.Empty<TElement>();
            _count = 0;
            
            Grow(source._count);
            source.CopyTo(_elements!, 0);
        }
        
#endregion

#region Properties
        
        public TKey Key => _key;

        public int Count => _count;

        /// <inheritdoc />
        bool ICollection<TElement>.IsReadOnly => false;

        /// <inheritdoc cref="IList{T}.this" />
        public TElement this[int index]
        {
            get => _elements![index];
            set => _elements![index] = value;
        }

#endregion

#region Public members
        
        /// <inheritdoc />
        public void Clear()
        {
            if (_elements != null)
            {
                Array.Clear(_elements, 0, _count);
                _count = 0;
            }
        }

        /// <inheritdoc />
        public int IndexOf(TElement item) => IndexOf(item, EqualityComparer<TElement>.Default);

        public int IndexOf(TElement item, IEqualityComparer<TElement> comparer)
        {
            if (_elements != null)
            {
                for (int i = 0; i < _elements.Length; i++)
                {
                    if (i >= _count) // Skip bound checks by looping over all elements
                        return -1;
                    if (comparer.Equals(_elements[i], item))
                        return i;
                }
            }

            return -1;
        }

        public int IndexOfLast(TElement item) => IndexOfLast(item, EqualityComparer<TElement>.Default);
        
        public int IndexOfLast(TElement item, IEqualityComparer<TElement> comparer)
        {
            if (_elements != null)
            {
                for (int i = _elements.Length - 1; i >= 0; i--)
                {
                    if (i >= _count) // Skip bound checks by looping over all elements
                        return -1;
                    if (comparer.Equals( _elements[i], item))
                        return i;
                }
            }

            return -1;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TElement item) => _elements != null && _elements.Contains(item);

        public bool ContainsAll(IEnumerable<TElement> items, IEqualityComparer<TElement> comparer)
        {
            if (_elements != null)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TElement item, IEqualityComparer<TElement> comparer) => IndexOf(item, comparer) >= 0;
        
        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(TElement item)
        {
            if (_elements == null || _count >= _elements.Length - 1)
                Grow(1);
            _elements![_count] = item;
        }

        public int AddRange(IEnumerable<TElement> items)
        {
            if (items is ICollection<TElement> collection)
            {
                int count = collection.Count;
                int last = _count;
                if (_elements == null || count >= _elements.Length - last)
                    Grow(count);
                collection.CopyTo(_elements!, _count);
                _count += count;
                return count;
            }

            int added = 0;
            foreach (TElement element in items)
            {
                Add(element);
                added++;
            }

            return added;
        }

        public void Insert(int index, TElement item)
        {
            if (_elements == null || _count > _elements.Length - 1)
                Grow(1);
            int remaining = _count - index;
            _elements.AsSpan(index, remaining).CopyTo(_elements.AsSpan(index + 1));
            _elements![index] = item;
            _count++;
        }
        
        public void Insert(int index, ReadOnlySpan<TElement> items)
        {
            if (_elements == null || _count > _elements.Length - items.Length)
                Grow(items.Length);
            int remaining = _count - index;
            _elements.AsSpan(index, remaining).CopyTo(_elements.AsSpan(index + items.Length));
            items.CopyTo(_elements.AsSpan(index, items.Length));
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

        /// <summary>Removes all occurrences of the specified <paramref name="item"/> from the grouping.</summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>The number of elements removed.</returns>
        public int RemoveAll(TElement item) => RemoveAll(item, EqualityComparer<TElement>.Default);

        /// <summary>Removes all occurrences of the specified <paramref name="item"/> from the grouping.</summary>
        /// <param name="item">The item to remove.</param>
        /// <param name="comparer">The comparer used to determine whether two elements are equal.</param>
        /// <returns>The number of elements removed.</returns>
        public int RemoveAll(TElement item, IEqualityComparer<TElement> comparer)
        {
            int removed = 0;
            int index;
            
            // Use IndexOfLast in the hope to reduce the amount of slow remove operations, by hitting the last index more often.
            while ((index = IndexOfLast(item, comparer)) >= 0)
            {
                RemoveAt(index);
                removed++;
            }

            return removed;
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
            
            _elements.AsSpan(next).CopyTo(rightPart);
            rightPart.CopyTo(_elements.AsSpan(index));
            
            ArrayPool<TElement>.Shared.Return(tempPool);
        }
        
        public void CopyTo(TElement[] array, int arrayIndex)
        {
            if (_elements != null)
                Array.Copy(_elements, 0, array, arrayIndex, _count);
        }

        public IEnumerator<TElement> GetEnumerator() => new ArraySegmentEnumerator<TElement>(_elements, 0, _count);

        IEnumerator IEnumerable.GetEnumerator() => new ArraySegmentEnumerator<TElement>(_elements, 0, _count);

        public int Resize()
        {
            int size = _elements != null ? _elements.Length : 0;
            
            if (_count == 0 && size != 0)
            {
                _elements = null;
                return 0;
            }
            
            if (_count < size && size > 4)
            {
                Array.Resize(ref _elements, _count);
            }

            return size;
        }

#endregion

#region Internal members
        
        private void Grow(int additionalCapacityBeyondCount)
        {
            if (_elements != null)
            {
                Debug.Assert(additionalCapacityBeyondCount > 0, "additionalCapacityBeyondCount > 0");
                Debug.Assert(_count > _elements.Length - additionalCapacityBeyondCount, "_count > _elements.Length - additionalCapacityBeyondCount");

                TElement[] array = new TElement[(int)Math.Max((uint)(_count + additionalCapacityBeyondCount), (uint)(_elements.Length * 2))];

                if (_elements.Length != 0)
                {
                    _elements.AsSpan(0, _count).CopyTo(array);
                    _elements = array;
                }
                else
                {
                    _elements = array;
                }
            }
            else
            {
                Debug.Assert(_count == 0, "_count == 0");
                _elements = new TElement[Math.Max(additionalCapacityBeyondCount, 4)];
            }
        }
        
#endregion
    }
}
