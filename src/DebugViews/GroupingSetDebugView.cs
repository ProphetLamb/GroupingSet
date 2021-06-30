using System.Collections.Generic;
using System.Diagnostics;

using KeyValueCollection.Grouping;

namespace KeyValueCollection.DebugViews
{
    internal sealed class GroupingSetDebugView<TKey, TElement>
        where TKey : notnull
    {
        private readonly GroupingSet<TKey, TElement> _set;

        public GroupingSetDebugView(GroupingSet<TKey, TElement> set)
        {
            _set = set;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public ValueGrouping<TKey, TElement>[] Items
        {
            get
            {
                var col = new ValueGrouping<TKey, TElement>[_set.Count];
                _set.CopyTo(col, 0);
                return col;
            }
        }
    }

    internal sealed class GroupingSetKeyCollectionDebugView<TKey, TValue>
    {
        private readonly ICollection<TKey> _collection;
 
        public GroupingSetKeyCollectionDebugView(ICollection<TKey> collection)
        {
            _collection = collection;
        }
 
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public TKey[] Items
        {
            get
            {
                TKey[] items = new TKey[_collection.Count];
                _collection.CopyTo(items, 0);
                return items;
            }
        }
    }
 
    internal sealed class GroupingSetValueCollectionDebugView<TKey, TValue>
    {
        private readonly ICollection<TValue> _collection;
 
        public GroupingSetValueCollectionDebugView(ICollection<TValue> collection)
        { 
            _collection = collection;
        }
 
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public TValue[] Items
        {
            get
            {
                TValue[] items = new TValue[_collection.Count];
                _collection.CopyTo(items, 0);
                return items;
            }
        }
    }
}
