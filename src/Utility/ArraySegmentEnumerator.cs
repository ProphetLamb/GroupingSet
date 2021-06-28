using System.Collections;
using System.Collections.Generic;

namespace KeyValueCollection.Utility
{
    internal struct ArraySegmentEnumerator<TItem> : IEnumerator<TItem>
    {
        private readonly TItem[]? _elements;
        private readonly int _startIndex;
        private readonly int _count;
        private TItem _current;
        private int _index;

        internal ArraySegmentEnumerator(TItem[]? elements, int startIndex, int count)
        {
            _elements = elements;
            _index = _startIndex = startIndex;
            _count = count;
            _current = default!;
        }

        public bool MoveNext()
        {
            if (_elements != null && _index < _count)
            {
                _current = _elements[_index++];
                return true;
            }

            _current = default!;
            return false;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _index = _startIndex;
            _current = default!;
        }

        /// <inheritdoc />
        public TItem Current => _current;

        /// <inheritdoc />
        object? IEnumerator.Current => _current;

        /// <inheritdoc />
        public void Dispose() { }
    }
}
