using System.Collections.Generic;
using System.Linq;

namespace KeyValueSet
{
    public static class GroupingExtensions
    {
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
    }
}
