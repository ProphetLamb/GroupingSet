using System.Diagnostics;
using System.Linq;

namespace KeyValueCollection.DebugViews
{
    internal sealed class IGroupingDebugView<TKey, TElement>
    {
        private readonly IGrouping<TKey, TElement> _grouping;
        private TElement[]? _cachedValues;
 
        public IGroupingDebugView(in IGrouping<TKey, TElement> grouping)
        {
            _grouping = grouping;
        }
 
        public TKey Key => _grouping.Key;
 
        // The name of this property must alphabetically follow `Key` so the elements appear last in the display.
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public TElement[] Values => _cachedValues != null ? _cachedValues : _cachedValues = _grouping.ToArray();
    }
}
