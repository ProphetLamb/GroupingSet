using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace KeyValueSet
{
    public partial class GroupingSet<TKey, TElement>
    {
        public struct Enumerator : IEnumerator<IGrouping<TKey, TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            private int _index;
            private IGrouping<TKey, TElement> _current;

            internal Enumerator(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                _index = 0;
                _current = default!;
            }

            public bool MoveNext()
            {
                if (_version != _set._version)
                    throw new InvalidOperationException("_version != _set._version");

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)_index < (uint)_set.m_count)
                {
                    ref Entry entry = ref _set._entries![_index++];
                    if (entry.Next < -1)
                        continue;
                    _current = entry.Grouping;
                    return true;
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
            public IGrouping<TKey, TElement> Current => _current;

            /// <inheritdoc />
            object IEnumerator.Current => _current;

            /// <inheritdoc />
            public void Dispose() { }
        }

        public struct DictionaryEnumerator : IEnumerator<KeyValuePair<TKey, IEnumerable<TElement>>>
        {
            private Enumerator _en;


            /// <inheritdoc />
            public DictionaryEnumerator(Enumerator en)
                : this()
            {
                _en = en;
            }

            /// <inheritdoc />
            public bool MoveNext() => _en.MoveNext();

            /// <inheritdoc />
            public void Reset() => _en.Reset();

            /// <inheritdoc />
            public KeyValuePair<TKey, IEnumerable<TElement>> Current => new(_en.Current.Key, _en.Current);

            /// <inheritdoc />
            object IEnumerator.Current => Current;

            /// <inheritdoc />
            public void Dispose()
            {
                _en.Dispose();
            }
        }

        public sealed class KeyCollection : ICollection<TKey>
        {
            private readonly GroupingSet<TKey, TElement> _set;

            public KeyCollection(GroupingSet<TKey, TElement> set)
            {
                _set = set;
            }


            /// <inheritdoc />
            public IEnumerator<TKey> GetEnumerator()
            {
                return _set.Select<IGrouping<TKey, TElement>, TKey>(g => g.Key).GetEnumerator();
            }

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            /// <inheritdoc />
            public void Add(TKey item) => _set.CreateIfNotPresent(item, out _);

            /// <inheritdoc />
            public void Clear() => _set.Clear();

            /// <inheritdoc />
            public bool Contains(TKey item) => _set.ContainsKey(item);

            /// <inheritdoc />
            public void CopyTo(TKey[] array, int arrayIndex)
            {
                foreach (IGrouping<TKey, TElement> g in _set)
                    array[arrayIndex++] = g.Key;
            }

            /// <inheritdoc />
            public bool Remove(TKey item) => _set.Remove(item);

            /// <inheritdoc />
            public int Count => _set.Count;

            /// <inheritdoc />
            public bool IsReadOnly => false;
        }
        
        public sealed class ValueCollection : ICollection<IEnumerable<TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;

            public ValueCollection(GroupingSet<TKey, TElement> set)
            {
                _set = set;
            }

            /// <inheritdoc />
            public int Count => _set.Count;

            /// <inheritdoc />
            public bool IsReadOnly => true;

            /// <inheritdoc />
            public IEnumerator<IEnumerable<TElement>> GetEnumerator()
            {
                return _set.Select<IGrouping<TKey, TElement>, IEnumerable<TElement>>(g => g).GetEnumerator();
            }

            /// <inheritdoc />
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            /// <inheritdoc />
            void ICollection<IEnumerable<TElement>>.Add(IEnumerable<TElement> item) => throw new NotSupportedException();

            /// <inheritdoc />
            void ICollection<IEnumerable<TElement>>.Clear() => throw new NotSupportedException();

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Contains(IEnumerable<TElement> item) => throw new NotSupportedException();

            /// <inheritdoc />
            public void CopyTo(IEnumerable<TElement>[] array, int arrayIndex)
            {
                foreach (IGrouping<TKey, TElement> g in _set)
                    array[arrayIndex++] = g;
            }

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Remove(IEnumerable<TElement> item) => throw new NotSupportedException();
        }
    }
}
