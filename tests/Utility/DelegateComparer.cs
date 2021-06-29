using System;
using System.Collections.Generic;

namespace KeyValueCollection.Tests.Utility
{
    public class DelegateComparer<T> : IEqualityComparer<T>
    {
        public Func<T, T, bool> EqualityFunc { get; }
        public Func<T, int> HashFunc { get;}

        public DelegateComparer(Func<T, T, bool> equalityFunc, Func<T, int> hashFunc)
        {
            EqualityFunc = equalityFunc;
            HashFunc = hashFunc;
        }

        /// <inheritdoc />
        public bool Equals(T x, T y) => EqualityFunc(x, y);

        /// <inheritdoc />
        public int GetHashCode(T obj) => HashFunc(obj);
    }
}
