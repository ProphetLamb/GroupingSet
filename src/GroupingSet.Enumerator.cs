using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
                        _current = new KeyValuePair<TKey, IEnumerable<TElement>>(entry.Key, entry.GetSegment());
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

        internal struct KeyEnumerator : IEnumerator<TKey>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            internal int Index;
            internal TKey CurrentValue;

            internal KeyEnumerator(GroupingSet<TKey, TElement> set)
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
                        CurrentValue = entry.KeyValue;
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
            public TKey Current => CurrentValue;

            /// <inheritdoc />
            object IEnumerator.Current => CurrentValue;

            /// <inheritdoc />
            public void Dispose() { }
        }

        internal struct ValueEnumerator : IEnumerator<IEnumerable<TElement>>
        {
            private readonly GroupingSet<TKey, TElement> _set;
            private readonly int _version;
            internal int Index;
            internal IEnumerable<TElement> CurrentValue;

            internal ValueEnumerator(GroupingSet<TKey, TElement> set)
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
                        CurrentValue = entry.GetSegment();
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
            public IEnumerable<TElement> Current => CurrentValue;

            /// <inheritdoc />
            object IEnumerator.Current => CurrentValue;

            /// <inheritdoc />
            public void Dispose() { }
        }
    }
}
