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
    public sealed class ImmutableGrouping<TKey, TElement> :
        IGrouping<TKey, TElement>,
        ICollection<TElement>,
        IReadOnlyList<TElement>,
        IEquatable<ImmutableGrouping<TKey, TElement>>,
        IEquatable<IGrouping<TKey, TElement>>
    {
#region Fields

        private readonly TElement[] _elements;
        private readonly int _hashCode;
        private readonly int _count;
        
#endregion

#region Ctors

        internal ImmutableGrouping(IEnumerable<TElement> elements, TKey key, int keyHashCode)
        {
            _elements = elements.ToArray();
            _count = _elements.Length;
            _hashCode = keyHashCode;
            Key = key;
        }

        internal ImmutableGrouping(ReadOnlySpan<TElement> elements, TKey key, int keyHashCode)
        {
            _elements = elements.ToArray();
            _count = _elements.Length;
            _hashCode = keyHashCode;
            Key = key;
        }
        
#endregion

#region Properties
        
        /// <inheritdoc />
        public TKey Key { get; }

        /// <inheritdoc cref="ICollection{T}.Count" />
        public int Count => _count;

        /// <inheritdoc />
        public bool IsReadOnly => true;

        /// <inheritdoc />
        public TElement this[int index]
        {
            get
            {
                if ((uint)_count >= (uint)index)
                    ThrowHelper.ThrowIndexOutOfRangeException();
                return _elements[index];
            }
        }

#endregion

#region Public members

        /// <inheritdoc />
        public IEnumerator<TElement> GetEnumerator() => new ArraySegmentEnumerator<TElement>(_elements, 0, _count);

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Not supported.</summary>
        /// <returns>Not supported.</returns>
        /// <exception cref="NotSupportedException">Not supported.</exception>
        void ICollection<TElement>.Add(TElement item) => throw ThrowHelper.GetNotSupportedException();

        /// <summary>Not supported.</summary>
        /// <returns>Not supported.</returns>
        /// <exception cref="NotSupportedException">Not supported.</exception>
        void ICollection<TElement>.Clear() => throw ThrowHelper.GetNotSupportedException();

        /// <inheritdoc />
        public bool Contains(TElement item) => _elements.Contains(item);

        /// <inheritdoc />
        public void CopyTo(TElement[] array, int arrayIndex) => _elements.CopyTo(array, arrayIndex);

        /// <summary>Not supported.</summary>
        /// <returns>Not supported.</returns>
        /// <exception cref="NotSupportedException">Not supported.</exception>
        bool ICollection<TElement>.Remove(TElement item) => throw ThrowHelper.GetNotSupportedException();

        /// <summary>Returns whether the <see cref="Key"/> is equal to the other <see cref="Key"/>.</summary>
        /// <param name="other">The object to compare with.</param>
        /// <returns><see langword="true"/> if the <see cref="Key"/>s are equal, otherwise; <see langword="false"/>.</returns>
        public bool Equals(ImmutableGrouping<TKey, TElement>? other)
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
             || obj is ImmutableGrouping<TKey, TElement> other && Equals(other)
             || obj is IGrouping<TKey, TElement> grouping && Equals(grouping);
        }

        /// <summary>Returns the hash-code of the <see cref="Key"/>.</summary>
        /// <returns>The hash-code of the <see cref="Key"/>.</returns>
        public override int GetHashCode() => _hashCode;

        public static bool operator ==(ImmutableGrouping<TKey, TElement>? left, ImmutableGrouping<TKey, TElement>? right) => Equals(left, right);

        public static bool operator !=(ImmutableGrouping<TKey, TElement>? left, ImmutableGrouping<TKey, TElement>? right) => !Equals(left, right);

#endregion
    }
}
