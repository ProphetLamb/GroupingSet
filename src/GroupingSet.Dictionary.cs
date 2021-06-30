using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using KeyValueCollection.DebugViews;
using KeyValueCollection.Exceptions;
using KeyValueCollection.Grouping;

namespace KeyValueCollection
{
    public partial class GroupingSet<TKey, TElement>
    {
#region IDictionary members

        /// <inheritdoc />
        IEnumerable<TKey> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.Keys => Keys;

        /// <inheritdoc />
        IEnumerable<IEnumerable<TElement>> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.Values => Values;

        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.IsReadOnly => false;

        /// <inheritdoc cref="IDictionary{TKey,TValue}.this" />
        IEnumerable<TElement> IDictionary<TKey, IEnumerable<TElement>>.this[TKey key]
        {
            get => this[key].GetSegment();
            set
            {
                this[key] = value switch {
                    ValueGrouping<TKey, TElement> val => new ValueGrouping<TKey, TElement>(val),
                    _ => new ValueGrouping<TKey, TElement>(key, Comparer.GetHashCode(key), value)
                };
            }
        }

        /// <inheritdoc />
        IEnumerable<TElement> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.this[TKey key] => this[key].GetSegment();

        /// <inheritdoc />
        void ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Add(KeyValuePair<TKey, IEnumerable<TElement>> item) => Add(item.Key, item.Value);

        /// <inheritdoc />
        void IDictionary<TKey, IEnumerable<TElement>>.Add(TKey key, IEnumerable<TElement> value) => Add(key, value);

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Contains(KeyValuePair<TKey, IEnumerable<TElement>> item) => GroupingContainsElements(item.Key, item.Value);

        /// <inheritdoc cref="IDictionary{TKey,TValue}.ContainsKey" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(TKey key) => FindItemIndex(key, out _) >= 0;

        /// <inheritdoc cref="IDictionary{TKey,TValue}.TryGetValue" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(TKey key, [NotNullWhen(true)] out IEnumerable<TElement> value)
        {
            int location = FindItemIndex(key, out _);
            if (location >= 0)
            {
                ref ValueGrouping<TKey, TElement> entry = ref _entries![location];
                value = entry.GetSegment();
                return true;
            }

            value = null!;
            return false;
        }

        /// <inheritdoc />
        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Remove(KeyValuePair<TKey, IEnumerable<TElement>> item)
        {
            if (_buckets == null)
                return false;

            int location = FindItemIndex(item.Key, out _);
            if (location < 0)
                return false;

            ref ValueGrouping<TKey, TElement> entry = ref _entries![location];
            var elementComparer = EqualityComparer<TElement>.Default;

            foreach (TElement element in item.Value)
                entry.Remove(element, elementComparer);

            return true;
        }

        /// <inheritdoc />
        void ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.CopyTo(KeyValuePair<TKey, IEnumerable<TElement>>[] array, int arrayIndex)
        {
            ValueGrouping<TKey, TElement>[]? entries = _entries;
            for (int i = 0; i < entries!.Length; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                array[i + arrayIndex] = new KeyValuePair<TKey, IEnumerable<TElement>>(entry.Key, entry.GetSegment());
            }
        }

#endregion

#region ILookup member

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TKey key) => ContainsKey(key);

        /// <inheritdoc />
        IEnumerable<TElement> ILookup<TKey, TElement>.this[TKey key] => this[key].GetSegment();

#endregion

        [DebuggerDisplay("Count: {Count}")]
        [DebuggerTypeProxy(typeof(GroupingSetKeyCollectionDebugView<,>))]
        internal sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
        {
            private readonly GroupingSet<TKey, TElement> _set;

            internal KeyCollection(GroupingSet<TKey, TElement> set)
            {
                _set = set;
            }

            /// <inheritdoc />
            public int Count => _set.Count;

            /// <inheritdoc />
            public bool IsReadOnly => false;

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => null!;

            /// <inheritdoc />
            public void Add(TKey item) => _set.CreateIfNotPresent(item, out _);

            /// <inheritdoc />
            public void Clear() => _set.Clear();

            /// <inheritdoc />
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Contains(TKey item) => _set.ContainsKey(item);

            void ICollection.CopyTo(Array array, int index)
            {
                CopyTo((TKey[])array, index);
            }

            /// <inheritdoc />
            public void CopyTo(TKey[] array, int arrayIndex)
            {
                using var en = new Enumerator(_set);
                while(en.MoveNext())
                    array[arrayIndex++] = en.CurrentValue.Key;
            }

            /// <inheritdoc />
            public bool Remove(TKey item) => _set.Remove(item);

            /// <inheritdoc />
            public IEnumerator<TKey> GetEnumerator() => new KeyEnumerator(_set);

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator() => new KeyEnumerator(_set);
        }

        [DebuggerDisplay("Count: {Count}")]
        [DebuggerTypeProxy(typeof(GroupingSetValueCollectionDebugView<,>))]
        internal sealed class ValueCollection : ICollection<IEnumerable<TElement>>, ICollection, IReadOnlyCollection<IEnumerable<TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;

            internal ValueCollection(GroupingSet<TKey, TElement> set)
            {
                _set = set;
            }

            /// <inheritdoc />
            public int Count => _set.Count;

            /// <inheritdoc />
            public bool IsReadOnly => true;

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => null!;

            /// <inheritdoc />
            void ICollection<IEnumerable<TElement>>.Add(IEnumerable<TElement> item) => ThrowHelper.ThrowNotSupportedException();

            /// <inheritdoc />
            void ICollection<IEnumerable<TElement>>.Clear() => ThrowHelper.ThrowNotSupportedException();

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Contains(IEnumerable<TElement> item)
            {
                ThrowHelper.ThrowNotSupportedException();
                return false;
            }

            /// <inheritdoc />
            public void CopyTo(IEnumerable<TElement>[] array, int arrayIndex)
            {
                var en = new Enumerator(_set);
                while (en.MoveNext())
                    array[arrayIndex++] = en.CurrentValue.GetSegment();
            }

            /// <inheritdoc />
            void ICollection.CopyTo(Array array, int index)
            {
                CopyTo((IEnumerable<TElement>[])array, index);
            }

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Remove(IEnumerable<TElement> item)
            {
                ThrowHelper.ThrowNotSupportedException();
                return false;
            }

            /// <inheritdoc />
            public IEnumerator<IEnumerable<TElement>> GetEnumerator() => new ValueEnumerator(_set);

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator() => new ValueEnumerator(_set);
        }
    }
}
