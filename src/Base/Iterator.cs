using System;
using System.Collections;
using System.Collections.Generic;

using KeyValueCollection.Exceptions;

namespace KeyValueCollection.Base
{
    /// <summary>
    /// A base class for enumerables that are loaded on-demand.
    /// </summary>
    /// <typeparam name="TSource">The type of each item to yield.</typeparam>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// The value of an iterator is immutable; the operation it represents cannot be changed.
    /// </description></item>
    /// <item><description>
    /// However, an iterator also serves as its own enumerator, so the state of an iterator
    /// may change as it is being enumerated.
    /// </description></item>
    /// <item><description>
    /// Hence, state that is relevant to an iterator's value should be kept in readonly fields.
    /// State that is relevant to an iterator's enumeration (such as the currently yielded item)
    /// should be kept in non-readonly fields.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal abstract class Iterator<TSource> : IEnumerable<TSource>, IEnumerator<TSource>
    {
        private readonly int _threadId;
        internal int _state;
        internal TSource _current = default!;

        /// <summary>
        /// Initializes a new instance of the <see cref="Iterator{TSource}"/> class.
        /// </summary>
        protected Iterator()
        {
            _threadId = Environment.CurrentManagedThreadId;
        }

        /// <summary>
        /// The item currently yielded by this iterator.
        /// </summary>
        public TSource Current => _current;

        /// <summary>
        /// Makes a shallow copy of this iterator.
        /// </summary>
        /// <remarks>
        /// This method is called if <see cref="GetEnumerator"/> is called more than once.
        /// </remarks>
        public abstract Iterator<TSource> Clone();

        /// <summary>
        /// Puts this iterator in a state whereby no further enumeration will take place.
        /// </summary>
        /// <remarks>
        /// Derived classes should override this method if necessary to clean up any
        /// mutable state they hold onto (for example, calling Dispose on other enumerators).
        /// </remarks>
        public virtual void Dispose()
        {
            _current = default!;
            _state = -1;
        }

        /// <summary>
        /// Gets the enumerator used to yield values from this iterator.
        /// </summary>
        /// <remarks>
        /// If <see cref="GetEnumerator"/> is called for the first time on the same thread
        /// that created this iterator, the result will be this iterator. Otherwise, the result
        /// will be a shallow copy of this iterator.
        /// </remarks>
        public IEnumerator<TSource> GetEnumerator()
        {
            Iterator<TSource> enumerator = _state == 0 && _threadId == Environment.CurrentManagedThreadId ? this : Clone();
            enumerator._state = 1;
            return enumerator;
        }

        /// <summary>
        /// Retrieves the next item in this iterator and yields it via <see cref="Current"/>.
        /// </summary>
        /// <returns><c>true</c> if there was another value to be yielded; otherwise, <c>false</c>.</returns>
        public abstract bool MoveNext();

        object? IEnumerator.Current => Current;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void IEnumerator.Reset() => ThrowHelper.ThrowNotSupportedException();
    }
}
