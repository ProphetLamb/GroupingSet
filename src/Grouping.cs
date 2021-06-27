using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

using KeyValueSet.DebugViews;

namespace KeyValueSet
{
    [DebuggerDisplay("Key: \"{Key}\", Count: {Count}")]
    [DebuggerTypeProxy(typeof(IGroupingDebugView<,>))]
    public class Grouping<TKey, TElement> : 
        IGrouping<TKey, TElement>,
        IList<TElement>,
        IReadOnlyList<TElement>,
        IDisposable
        where TKey : notnull
    {
#region Fields

        internal readonly TKey _key;
        internal readonly int _hashCode;
        private TElement[] _elements;
        internal int _count;
        
#endregion

#region Ctors

        internal Grouping(TKey key, int hashCode)
        {
            _key = key;
            _hashCode = hashCode;
            _elements = Array.Empty<TElement>();
            _count = 0;
        }

        internal Grouping(TKey key, int hashCode, IEnumerable<TElement> elements)
        {
            _key = key;
            _hashCode = hashCode;

            switch (elements)
            {
                case ICollection<TElement> collection: {
                    _elements = ArrayPool<TElement>.Shared.Rent(collection.Count);
                    _count = collection.Count;
                    collection.CopyTo(_elements, 0);
                } break;
                default: {
                    _elements = Array.Empty<TElement>();
                    _count = 0;
                    AddRange(elements);
                } break;
            }
        }

        internal Grouping(Grouping<TKey, TElement> source)
        {
            _key = source._key;
            _hashCode = source._hashCode;
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(TElement item)
        {
            if (_count >= _elements.Length - 1)
                Grow(1);
            _elements![_count] = item;
        }
        
        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Array.Clear(_elements, 0, _count);
            _count = 0;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TElement item) => _elements.Contains(item);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsAll(IEnumerable<TElement> items, IEqualityComparer<TElement> comparer)
        {
            foreach (TElement element in items)
            {
                if (!Contains(element, comparer))
                    return false;
            }

            return true;
        }

        public int AddRange(IEnumerable<TElement> items)
        {
            if (items is ICollection<TElement> collection)
            {
                int count = collection.Count;
                int last = _count;
                if (count >= _elements.Length - last)
                    Grow(count);
                collection.CopyTo(_elements!, _count);
                _count += count;
                return count;
            }
            return AddRange(items.ToArray().AsSpan());
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddRange(ReadOnlySpan<TElement> items)
        {
            int count = items.Length;
            int last = _count;
            if (count >= _elements.Length - last)
                Grow(count);
            items.CopyTo(_elements.AsSpan(_count));
            _count += count;
            return count;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(TElement item) => IndexOf(item, EqualityComparer<TElement>.Default);

        public void Insert(int index, TElement item)
        {
            if (_count > _elements.Length - 1)
                Grow(1);
            int remaining = _count - index;
            _elements.AsSpan(index, remaining).CopyTo(_elements.AsSpan(index + 1));
            _elements![index] = item;
            _count++;
        }
        
        public void Insert(int index, ReadOnlySpan<TElement> items)
        {
            if (_count > _elements.Length - items.Length)
                Grow(items.Length);
            int remaining = _count - index;
            _elements.AsSpan(index, remaining).CopyTo(_elements.AsSpan(index + items.Length));
            items.CopyTo(_elements.AsSpan(index, items.Length));
            _count += items.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TElement item, IEqualityComparer<TElement> comparer) => IndexOf(item, comparer) >= 0;

        /// <inheritdoc cref="ICollection{T}.Remove(T)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(TElement item, IEqualityComparer<TElement> comparer)
        {
            int index = IndexOf(item, comparer);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(TElement item) => Remove(item, EqualityComparer<TElement>.Default);


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
            TElement[] rightPair = ArrayPool<TElement>.Shared.Rent(count - next);
            
            _elements.AsSpan(next).CopyTo(rightPair);
            rightPair.CopyTo(_elements.AsSpan(index));
            
            ArrayPool<TElement>.Shared.Return(rightPair);
        }

        public int IndexOf(TElement item, IEqualityComparer<TElement> comparer)
        {
            for (int i = 0; i < _elements.Length; i++)
            {
                if (i >= _count) // Skip bound checks by looping over all elements
                    return -1;
                if (comparer.Equals( _elements[i], item))
                    return i;
            }

            return -1;
        }

        public int IndexOfLast(TElement item, IEqualityComparer<TElement> comparer)
        {
            for (int i = _elements.Length - 1; i >= 0; i--)
            {
                if (i >= _count) // Skip bound checks by looping over all elements
                    return -1;
                if (comparer.Equals( _elements[i], item))
                    return i;
            }

            return -1;
        }
        
        public void CopyTo(TElement[] array, int arrayIndex)
        {
            Array.Copy(_elements, 0, array, arrayIndex, _count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            ArrayPool<TElement>.Shared.Return(_elements);
            _elements = Array.Empty<TElement>();
        }

        public override int GetHashCode() => _hashCode;

        public IEnumerator<TElement> GetEnumerator() => ((IEnumerable<TElement>)_elements!).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _elements!.GetEnumerator();


        public static Grouping<TKey, TElement> From(IGrouping<TKey, TElement> grouping) => From(grouping, EqualityComparer<TKey>.Default);
        
        public static Grouping<TKey, TElement> From(IGrouping<TKey, TElement> grouping, IEqualityComparer<TKey> hashCodeCreator)
        {
            if (grouping is Grouping<TKey, TElement> other)
                return new Grouping<TKey, TElement>(other);
            return new Grouping<TKey, TElement>(grouping.Key, hashCodeCreator.GetHashCode(grouping.Key), grouping);
        }
        
#endregion

#region Internal members
        
        private void Grow(int additionalCapacityBeyondCount)
        {
            Debug.Assert(additionalCapacityBeyondCount > 0, "additionalCapacityBeyondCount > 0");
            Debug.Assert(_count > _elements.Length - additionalCapacityBeyondCount, "_count > _elements.Length - additionalCapacityBeyondCount");

            TElement[] poolArray = ArrayPool<TElement>.Shared.Rent((int)Math.Max((uint)(_count + additionalCapacityBeyondCount), (uint)(_elements.Length * 2)));

            if (_elements.Length == 0)
            {
                _elements = poolArray;
            }
            else
            {
                _elements.AsSpan(0, _count).CopyTo(poolArray);
                TElement[] returnToPool = _elements;
                _elements = poolArray;
                ArrayPool<TElement>.Shared.Return(returnToPool);
            }
        }
        
#endregion
    }
}
