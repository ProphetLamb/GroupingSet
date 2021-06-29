using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using KeyValueCollection.DebugViews;
using KeyValueCollection.Exceptions;
using KeyValueCollection.Grouping;

namespace KeyValueCollection
{
    public partial class GroupingSet<TKey, TElement>
    {
        internal struct Enumerator : IEnumerator<IGrouping<TKey, TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            internal int Index;
            internal ValueGrouping<TKey, TElement> CurrentValue;

            internal Enumerator(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                Index = 0;
                CurrentValue = default!;
            }

            public bool MoveNext()
            {
                if (_version != _set._version)
                    ThrowHelper.ThrowInvalidOperationException_EnumeratorVersionDiffers();

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)Index < (uint)_set.m_count)
                {
                    ref ValueGrouping<TKey, TElement> entry = ref _set._entries![Index++];
                    if (entry.Next >= -1)
                    {
                        CurrentValue = entry;
                        return true;
                    }
                }

                Index = _set.m_count + 1;
                CurrentValue = default!;
                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                Index = 0;
                CurrentValue = default!;
            }

            /// <inheritdoc />
            public IGrouping<TKey, TElement> Current => CurrentValue;

            /// <inheritdoc />
            object IEnumerator.Current => CurrentValue;

            /// <inheritdoc />
            public void Dispose() { }
        }

        internal struct DictionaryEnumerator : IEnumerator<KeyValuePair<TKey, IEnumerable<TElement>>>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, IEnumerable<TElement>> _current;

            internal DictionaryEnumerator(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                _index = 0;
                _current = default!;
            }

            public bool MoveNext()
            {
                if (_version != _set._version)
                    ThrowHelper.ThrowInvalidOperationException_EnumeratorVersionDiffers();

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)_index < (uint)_set.m_count)
                {
                    ref ValueGrouping<TKey, TElement> entry = ref _set._entries![_index++];
                    if (entry.Next >= -1)
                    {
                        _current = new KeyValuePair<TKey, IEnumerable<TElement>>(entry.Key, entry.Elements ?? Enumerable.Empty<TElement>());
                        return true;
                    }
                }

                _index = _set.m_count + 1;
                _current = default!;
                return false;
            }

            /// <inheritdoc />
            public void Reset()
            {
                _index = 0;
                _current = default!;
            }

            /// <inheritdoc />
            public KeyValuePair<TKey, IEnumerable<TElement>> Current => _current;

            /// <inheritdoc />
            object IEnumerator.Current => _current;

            /// <inheritdoc />
            public void Dispose() { }
        }

        [DebuggerDisplay("Count: {Count}")]
        [DebuggerTypeProxy(typeof(ICollectionDebugView<>))]
        internal sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
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

            /// <inheritdoc />
            public void Add(TKey item) => _set.CreateIfNotPresent(item, out _);

            /// <inheritdoc />
            public void Clear() => _set.Clear();

            /// <inheritdoc />
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Contains(TKey item) => _set.ContainsKey(item);

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
            public IEnumerator<TKey> GetEnumerator()
            {
                using var en = new Enumerator(_set);
                while(en.MoveNext())
                    yield return en.CurrentValue.Key;
            }

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [DebuggerDisplay("Count: {Count}")]
        [DebuggerTypeProxy(typeof(ICollectionDebugView<>))]
        internal sealed class ValueCollection : ICollection<IEnumerable<TElement>>, IReadOnlyCollection<IEnumerable<TElement>>
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

            /// <inheritdoc />
            void ICollection<IEnumerable<TElement>>.Add(IEnumerable<TElement> item) =>throw ThrowHelper.GetNotSupportedException();

            /// <inheritdoc />
            void ICollection<IEnumerable<TElement>>.Clear() => throw ThrowHelper.GetNotSupportedException();

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Contains(IEnumerable<TElement> item) => throw ThrowHelper.GetNotSupportedException();

            /// <inheritdoc />
            public void CopyTo(IEnumerable<TElement>[] array, int arrayIndex)
            {
                var en = new Enumerator(_set);
                while (en.MoveNext())
                    array[arrayIndex++] = en.CurrentValue.Elements ?? Enumerable.Empty<TElement>();
            }

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Remove(IEnumerable<TElement> item) => throw ThrowHelper.GetNotSupportedException();

            /// <inheritdoc />
            public IEnumerator<IEnumerable<TElement>> GetEnumerator()
            {
                var en = new Enumerator(_set);
                while (en.MoveNext())
                    yield return en.CurrentValue;
            }

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
