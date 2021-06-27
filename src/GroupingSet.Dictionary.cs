using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace KeyValueSet
{
    public partial class GroupingSet<TKey, TElement>
    {
#region IDictionary members

        /// <inheritdoc />
        IEnumerable<TKey> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.Keys => _keys!;
        
        ICollection<IEnumerable<TElement>> IDictionary<TKey, IEnumerable<TElement>>.Values => new ValueCollection(this);

        /// <inheritdoc />
        IEnumerable<IEnumerable<TElement>> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.Values => this;

        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.IsReadOnly => false;

        /// <inheritdoc cref="IDictionary{TKey,TValue}.this" />
        IEnumerable<TElement> IDictionary<TKey, IEnumerable<TElement>>.this[TKey key]
        {
            get => this[key];
            set => this[key] = new Grouping<TKey, TElement>(key, Comparer.GetHashCode(key), value);
        }

        /// <inheritdoc />
        IEnumerable<TElement> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.this[TKey key] => this[key];

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Add(KeyValuePair<TKey, IEnumerable<TElement>> item) => Add(item.Key, item.Value);

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void IDictionary<TKey, IEnumerable<TElement>>.Add(TKey key, IEnumerable<TElement> value) => Add(key, value);

        /// <inheritdoc />
        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Contains(KeyValuePair<TKey, IEnumerable<TElement>> item)
        {
            if (FindItemIndex(item.Key, out int location) < 0)
                return false;
            ref Entry entry = ref _entries![location];
            var elementComparer = EqualityComparer<TElement>.Default;

            foreach (TElement element in item.Value)
            {
                if (!entry.Grouping.Contains(element, elementComparer))
                    return false;
            }

            return true;
        }

        /// <inheritdoc cref="IDictionary{TKey,TValue}.ContainsKey" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(TKey key) => FindItemIndex(key, out _) >= 0;

        /// <inheritdoc cref="IDictionary{TKey,TValue}.TryGetValue" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IReadOnlyDictionary<TKey, IEnumerable<TElement>>.TryGetValue(TKey key, [NotNullWhen(true)] out IEnumerable<TElement>? value)
        {
            bool success = TryGetValue(key, out Grouping<TKey, TElement>? grouping);
            value = grouping;
            return success;
        }

        /// <inheritdoc cref="IDictionary{TKey,TValue}.TryGetValue" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IDictionary<TKey, IEnumerable<TElement>>.TryGetValue(TKey key, [NotNullWhen(true)] out IEnumerable<TElement>? value)
        {
            bool success = TryGetValue(key, out Grouping<TKey, TElement>? grouping);
            value = grouping;
            return success;
        }

        /// <inheritdoc />
        bool ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.Remove(KeyValuePair<TKey, IEnumerable<TElement>> item)
        {
            if (_buckets == null)
                return false;

            int location = FindItemIndex(item.Key, out _);
            if (location < 0)
                return false;
            
            ref Entry entry = ref _entries![location];
            var elementComparer = EqualityComparer<TElement>.Default;

            foreach (TElement element in item.Value)
                entry.Grouping.RemoveAll(element, elementComparer);

            return true;
        }

        /// <inheritdoc />
        void ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.CopyTo(KeyValuePair<TKey, IEnumerable<TElement>>[] array, int arrayIndex)
        {
            Entry[] entries = _entries!;
            for (int i = 0; i < entries.Length; i++)
            {
                ref Entry entry = ref _entries![i];
                array[i + arrayIndex] = new KeyValuePair<TKey, IEnumerable<TElement>>(entry.Grouping._key, entry.Grouping);
            }
        }

#endregion

#region ILookup member

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TKey key) => ContainsKey(key);

        /// <inheritdoc />
        IEnumerable<TElement> ILookup<TKey, TElement>.this[TKey key] => this[key];

#endregion
    }
}
