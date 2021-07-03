using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using KeyValueCollection.Grouping;

namespace KeyValueCollection.Extensions
{
    public static class GroupingExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlyGrouping<TKey, TElement> ToImmutable<TKey, TElement>(this Grouping<TKey, TElement> grouping)
            where TKey : notnull
        {
            return new(grouping.Elements, 0, grouping.Count, grouping.Key, grouping.HashCode);
        }

        public static ReadOnlyGrouping<TKey, TElement> ToImmutable<TKey, TElement>(this IGrouping<TKey, TElement> grouping)
            where TKey : notnull
        {
            var array = grouping.ToArray();
            return new(array, 0, array.Length, grouping.Key, grouping.Key.GetHashCode());
        }

        public static ReadOnlyGrouping<TKey, TElement> ToImmutable<TKey, TElement>(this IGrouping<TKey, TElement> grouping, IEqualityComparer<TKey> hashCodeCreator)
            where TKey : notnull
        {
            var array = grouping.ToArray();
            return new(array, 0, array.Length, grouping.Key, hashCodeCreator.GetHashCode(grouping.Key));
        }
    }
}
