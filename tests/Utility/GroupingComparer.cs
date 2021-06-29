using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace KeyValueCollection.Tests.Utility
{
    public sealed class GroupingComparer : IEqualityComparer<IGrouping<Person, Vector3>>
    {
        private static readonly Lazy<GroupingComparer> s_default = new(() => new GroupingComparer());

        public static GroupingComparer Default => s_default.Value!;

        public bool Equals(IGrouping<Person, Vector3> x, IGrouping<Person, Vector3> y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (ReferenceEquals(x, null))
                return false;
            if (ReferenceEquals(y, null))
                return false;
            if (x.GetType() != y.GetType())
                return false;
            return PersonComparer.Default.Equals(x.Key, y.Key);
        }

        public int GetHashCode(IGrouping<Person, Vector3> obj)
        {
            return PersonComparer.Default.GetHashCode(obj.Key);
        }
    }
}
