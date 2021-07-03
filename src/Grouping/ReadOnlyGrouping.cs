using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using KeyValueCollection.DebugViews;
using KeyValueCollection.Exceptions;
using KeyValueCollection.Utility;

namespace KeyValueCollection.Grouping
{
    [DebuggerDisplay("Key: {Key}, Count: {Count}")]
    [DebuggerTypeProxy(typeof(IGroupingDebugView<,>))]
    public sealed class ReadOnlyGrouping<TKey, TElement> :
        IGrouping<TKey, TElement>,
        ICollection<TElement>,
        ICollection,
        IReadOnlyList<TElement>,
        IEquatable<ReadOnlyGrouping<TKey, TElement>>,
        IEquatable<IGrouping<TKey, TElement>>
    {
#region Fields

        private readonly TElement[]? _elements;
        private readonly int _startIndex;
        private readonly int _endIndex;
        private readonly int _hashCode;
        
#endregion

#region Ctors

        internal ReadOnlyGrouping(TElement[]? elements, int startIndex, int endIndex, TKey key, int keyHashCode)
        {
            _elements = elements;
            _startIndex = startIndex;
            _endIndex = endIndex;
            _hashCode = keyHashCode;
            Key = key;
        }
        
#endregion

#region Properties
        
        /// <inheritdoc />
        public TKey Key { get; }

        /// <inheritdoc cref="ICollection{T}.Count" />
        public int Count => _endIndex - _startIndex;

        /// <inheritdoc />
        public bool IsSynchronized => true;
        
        /// <inheritdoc />
        public object SyncRoot => _elements!;

        /// <inheritdoc />
        public bool IsReadOnly => true;

        /// <inheritdoc />
        public TElement this[int index]
        {
            get
            {
                if ((uint)(_endIndex - _startIndex) >= (uint)index)
                    ThrowHelper.ThrowIndexOutOfRangeException();
                return _elements![index - _startIndex];
            }
        }

#endregion

#region Public members

        /// <inheritdoc />
        public IEnumerator<TElement> GetEnumerator() => new ArraySegmentEnumerator<TElement>(_elements, _startIndex, _endIndex);

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => new ArraySegmentEnumerator<TElement>(_elements, _startIndex, _endIndex);

        /// <summary>Not supported.</summary>
        /// <returns>Not supported.</returns>
        /// <exception cref="NotSupportedException">Not supported.</exception>
        void ICollection<TElement>.Add(TElement item) => ThrowHelper.ThrowNotSupportedException();

        /// <summary>Not supported.</summary>
        /// <returns>Not supported.</returns>
        /// <exception cref="NotSupportedException">Not supported.</exception>
        void ICollection<TElement>.Clear() => ThrowHelper.ThrowNotSupportedException();

        /// <inheritdoc />
        public bool Contains(TElement item)
        {
            IEqualityComparer<TElement> comparer = EqualityComparer<TElement>.Default;
            for (int i = _startIndex; i < _endIndex; i++)
            {
                if (!comparer.Equals(item, _elements![i]))
                    continue;
                return true;
            }
            return false;
        }

        /// <inheritdoc />
        public void CopyTo(TElement[] array, int arrayIndex) => _elements?.CopyTo(array, arrayIndex);

        /// <inheritdoc />
        void ICollection.CopyTo(Array array, int index) => CopyTo((TElement[]) array, index);

        /// <summary>Not supported.</summary>
        /// <returns>Not supported.</returns>
        /// <exception cref="NotSupportedException">Not supported.</exception>
        bool ICollection<TElement>.Remove(TElement item)
        {
            ThrowHelper.ThrowNotSupportedException();
            return false;
        }


        /// <summary>Returns whether the <see cref="Key"/> is equal to the other <see cref="Key"/>.</summary>
        /// <param name="other">The object to compare with.</param>
        /// <returns><see langword="true"/> if the <see cref="Key"/>s are equal, otherwise; <see langword="false"/>.</returns>
        public bool Equals(ReadOnlyGrouping<TKey, TElement>? other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return EqualityComparer<TKey>.Default.Equals(Key, other.Key);
        }


        /// <summary>Returns whether the <see cref="Key"/> is equal to the other <see cref="Key"/>.</summary>
        /// <param name="other">The object to compare with.</param>
        /// <returns><see langword="true"/> if the <see cref="Key"/>s are equal, otherwise; <see langword="false"/>.</returns>
        public bool Equals(IGrouping<TKey, TElement>? other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return EqualityComparer<TKey>.Default.Equals(Key, other.Key);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj)
             || obj is ReadOnlyGrouping<TKey, TElement> other && Equals(other)
             || obj is IGrouping<TKey, TElement> grouping && Equals(grouping);
        }

        /// <summary>Returns the hash-code of the <see cref="Key"/>.</summary>
        /// <returns>The hash-code of the <see cref="Key"/>.</returns>
        public override int GetHashCode() => _hashCode;

        public static bool operator ==(ReadOnlyGrouping<TKey, TElement>? left, ReadOnlyGrouping<TKey, TElement>? right) => Equals(left, right);

        public static bool operator !=(ReadOnlyGrouping<TKey, TElement>? left, ReadOnlyGrouping<TKey, TElement>? right) => !Equals(left, right);

#endregion
    }
}
