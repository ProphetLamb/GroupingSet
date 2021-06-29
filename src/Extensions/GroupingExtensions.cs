using System;
using System.Collections.Generic;
using System.Linq;

using KeyValueCollection.Grouping;

namespace KeyValueCollection.Extensions
{
    public static class GroupingExtensions
    {
        internal static ImmutableGrouping<TKey, TElement> ToImmutable<TKey, TElement>(this ValueGrouping<TKey, TElement> grouping)
            where TKey : notnull
        {
            if (grouping.Elements != null)
                return new(grouping.Elements.AsSpan(0, grouping.Count), grouping.Key, grouping.HashCode);
            return new(Enumerable.Empty<TElement>(), grouping.Key, grouping.HashCode);
        }

        internal static ImmutableGrouping<TKey, TElement> ToImmutable<TKey, TElement>(this IGrouping<TKey, TElement> grouping)
            where TKey : notnull
        {
            return new(grouping, grouping.Key, grouping.Key.GetHashCode());
        }

        internal static ImmutableGrouping<TKey, TElement> ToImmutable<TKey, TElement>(this IGrouping<TKey, TElement> grouping, IEqualityComparer<TKey> hashCodeCreator)
            where TKey : notnull
        {
            return new(grouping, grouping.Key, hashCodeCreator.GetHashCode(grouping.Key));
        }
    }
}
