using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

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
            get => this[key].Elements ?? Enumerable.Empty<TElement>();
            set
            {
                this[key] = value switch {
                    ValueGrouping<TKey, TElement> val => new ValueGrouping<TKey, TElement>(val),
                    _ => new ValueGrouping<TKey, TElement>(key, Comparer.GetHashCode(key), value)
                };
            }
        }

        /// <inheritdoc />
        IEnumerable<TElement> IReadOnlyDictionary<TKey, IEnumerable<TElement>>.this[TKey key] => this[key].Elements ?? Enumerable.Empty<TElement>();

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
        bool IReadOnlyDictionary<TKey, IEnumerable<TElement>>.TryGetValue(TKey key, [NotNullWhen(true)] out IEnumerable<TElement>? value)
        {
            int location = FindItemIndex(key, out _);
            if (location >= 0)
            {
                value = _entries![location].Elements ?? Enumerable.Empty<TElement>();
                return true;
            }

            value = null;
            return false;
        }

        /// <inheritdoc cref="IDictionary{TKey,TValue}.TryGetValue" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IDictionary<TKey, IEnumerable<TElement>>.TryGetValue(TKey key, [NotNullWhen(true)] out IEnumerable<TElement>? value)
        {
            if (TryGetValue(key, out ValueGrouping<TKey, TElement>? grouping))
            {
                value = grouping.Value.Elements ?? Enumerable.Empty<TElement>();
                return true;
            }

            value = null;
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
                entry.RemoveAll(element, elementComparer);

            return true;
        }

        /// <inheritdoc />
        void ICollection<KeyValuePair<TKey, IEnumerable<TElement>>>.CopyTo(KeyValuePair<TKey, IEnumerable<TElement>>[] array, int arrayIndex)
        {
            ValueGrouping<TKey, TElement>[]? entries = _entries;
            for (int i = 0; i < entries!.Length; i++)
            {
                ref ValueGrouping<TKey, TElement> entry = ref entries[i];
                array[i + arrayIndex] = new KeyValuePair<TKey, IEnumerable<TElement>>(entry.Key, entry.Elements ?? Enumerable.Empty<TElement>());
            }
        }

#endregion

#region ILookup member

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TKey key) => ContainsKey(key);

        /// <inheritdoc />
        IEnumerable<TElement> ILookup<TKey, TElement>.this[TKey key] => this[key].Elements ?? Enumerable.Empty<TElement>();

#endregion
    }
}
