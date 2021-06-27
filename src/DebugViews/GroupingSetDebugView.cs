using System.Diagnostics;

namespace KeyValueSet.DebugViews
{
    public class GroupingSetDebugView<TKey, TElement>
        where TKey : notnull
    {
        private GroupingSet<TKey, TElement> _set;

        public GroupingSetDebugView(GroupingSet<TKey, TElement> set)
        {
            _set = set;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Grouping<TKey, TElement>[] Items
        {
            get
            {
                var col = new Grouping<TKey, TElement>[_set.Count];
                _set.CopyTo(col, 0);
                return col;
            }
        }
    }
}
