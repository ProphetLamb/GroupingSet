using System.Diagnostics;

using KeyValueCollection.Grouping;

namespace KeyValueCollection.DebugViews
{
    public class GroupingSetDebugView<TKey, TElement>
        where TKey : notnull
    {
        private KeyValueCollection.GroupingSet<TKey, TElement> _set;

        public GroupingSetDebugView(KeyValueCollection.GroupingSet<TKey, TElement> set)
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
}
