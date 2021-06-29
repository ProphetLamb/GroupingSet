using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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

        internal sealed class KeyCollection : Iterator<TKey>, ICollection<TKey>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            private readonly int _count;
            private int _index;

            public KeyCollection(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                _count = set.m_count;
            }

            /// <inheritdoc />
            public int Count => _count;

            /// <inheritdoc />
            public bool IsReadOnly => false;

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
            public override bool MoveNext()
            {
                if (_version != _set._version || _set._entries == null)
                    ThrowHelper.ThrowInvalidOperationException_EnumeratorVersionDiffers();
                
                if (_state >= 0 && (uint)_count <= (uint)_index)
                {
                    // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                    // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                    while ((uint)_index < (uint)_set.m_count)
                    {
                        ref ValueGrouping<TKey, TElement> entry = ref _set._entries![_index++];
                        if (entry.Next >= -1)
                        {
                            _current = entry._key;
                            return true;
                        }
                    }
                    return true;
                }

                return false;
            }

            /// <inheritdoc />
            public override Iterator<TKey> Clone() => new KeyCollection(_set);
        }
        
        internal sealed class ValueCollection : Iterator<IEnumerable<TElement>>, ICollection<IEnumerable<TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            private readonly int _count;
            private int _index;

            public ValueCollection(GroupingSet<TKey, TElement> set)
            {
                _set = set;
                _version = set._version;
                _count = set.m_count;
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
                foreach (IGrouping<TKey, TElement> g in _set)
                    array[arrayIndex++] = g;
            }

            /// <inheritdoc />
            bool ICollection<IEnumerable<TElement>>.Remove(IEnumerable<TElement> item) => throw ThrowHelper.GetNotSupportedException();

            /// <inheritdoc />
            public override bool MoveNext()
            {
                if (_version != _set._version || _set._entries == null)
                    ThrowHelper.ThrowInvalidOperationException_EnumeratorVersionDiffers();
                
                if (_state >= 0 && (uint)_count <= (uint)_index)
                {
                    // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                    // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                    while ((uint)_index < (uint)_set.m_count)
                    {
                        ref ValueGrouping<TKey, TElement> entry = ref _set._entries![_index++];
                        if (entry.Next >= -1)
                        {
                            _current = entry._elements ?? Enumerable.Empty<TElement>();
                            return true;
                        }
                    }
                    return true;
                }

                return false;
            }
            
            /// <inheritdoc />
            public override Iterator<IEnumerable<TElement>> Clone() => new ValueCollection(_set);
        }
    }
}
