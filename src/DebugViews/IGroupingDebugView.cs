using System.Diagnostics;
using System.Linq;

namespace KeyValueCollection.DebugViews
{
    internal sealed class IGroupingDebugView<TKey, TElement>
    {
        private readonly IGrouping<TKey, TElement> _grouping;

        public IGroupingDebugView(IGrouping<TKey, TElement> grouping)
        {
            _grouping = grouping;
        }

        public TKey Key => _grouping.Key;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public TElement[] Values => _grouping.ToArray();
    }
}
