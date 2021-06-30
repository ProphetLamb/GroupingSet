using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyValueCollection.Extensions
{
    public static class GroupingSetExtensions
    {
        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement, TCollection> (this IEnumerable<TCollection> sequence, Func<TCollection, TKey> keySelector)
            where TKey : notnull
            where TCollection : IEnumerable<TElement>
        {
            GroupingSet<TKey, TElement> set = new();
            foreach(TCollection item in sequence)
                set.Add(keySelector(item), item);
            return set;
        }

        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement, TCollection> (this IEnumerable<TCollection> sequence, Func<TCollection, TKey> keySelector, IEqualityComparer<TKey> comparer)
            where TKey : notnull
            where TCollection : IEnumerable<TElement>
        {
            GroupingSet<TKey, TElement> set = new(comparer);
            foreach(TCollection item in sequence)
                set.Add(keySelector(item), item);
            return set;
        }
        
        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement, TItem> (this IEnumerable<TItem> sequence, Func<TItem, TKey> keySelector, Func<TItem, IEnumerable<TElement>> elementSelector)
            where TKey : notnull
        {
            GroupingSet<TKey, TElement> set = new();
            foreach(TItem item in sequence)
                set.Add(keySelector(item), elementSelector(item));
            return set;
        }

        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement, TItem> (this IEnumerable<TItem> sequence, Func<TItem, TKey> keySelector, Func<TItem, IEnumerable<TElement>> elementSelector, IEqualityComparer<TKey> comparer)
            where TKey : notnull
        {
            GroupingSet<TKey, TElement> set = new(comparer);
            foreach(TItem item in sequence)
                set.Add(keySelector(item), elementSelector(item));
            return set;
        }

        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement> (this IEnumerable<IGrouping<TKey, TElement>> sequence)
            where TKey : notnull
        {
            return new(sequence);
        }

        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement> (this IEnumerable<IGrouping<TKey, TElement>> sequence, IEqualityComparer<TKey> comparer)
            where TKey : notnull
        {
            return new(sequence, comparer);
        }

        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement> (this Dictionary<TKey, TElement> dictionary)
            where TKey : notnull
        {
            return new(dictionary);
        }

        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement> (this Dictionary<TKey, TElement> dictionary, IEqualityComparer<TKey> comparer)
            where TKey : notnull
        {
            return new(dictionary, comparer);
        }

        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement, TCollection> (this Dictionary<TKey, TCollection> dictionary)
            where TKey : notnull
            where TCollection : IEnumerable<TElement>
        {
            GroupingSet<TKey, TElement> set = new();
            foreach(var pair in dictionary)
                set.Add(pair.Key, pair.Value);
            return set;
        }

        public static GroupingSet<TKey, TElement> ToSet<TKey, TElement, TCollection> (this Dictionary<TKey, TCollection> dictionary, IEqualityComparer<TKey> comparer)
            where TKey : notnull
            where TCollection : IEnumerable<TElement>
        {
            GroupingSet<TKey, TElement> set = new(comparer);
            foreach(var pair in dictionary)
                set.Add(pair.Key, pair.Value);
            return set;
        }
    }
}
