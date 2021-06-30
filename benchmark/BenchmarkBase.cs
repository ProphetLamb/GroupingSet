using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GenericRange;
using KeyValueCollection.Extensions;
using KeyValueCollection.Tests;
using KeyValueCollection.Tests.Utility;

namespace KeyValueCollection.Benchmark
{
    public abstract class BenchmarkBase
    {
        public Person[] People;
        public Vector3[][] Metrics;

        public HashSet<IGrouping<Person, Vector3>> HashSet;

        public Dictionary<Person, IList<Vector3>> ListDictionary;

        public GroupingSet<Person, Vector3> GroupingSet;

        public virtual (HashSet<IGrouping<Person, Vector3>> HashSet, Dictionary<Person, IList<Vector3>> Dictionary, GroupingSet<Person, Vector3> GroupingSet) GenerateSetsFromData(int count, int vectorFieldSize)
        {
            HashSet<IGrouping<Person, Vector3>> hashSet = new(count, GroupingComparer.Default);
#if BENCH_HASHSET
            for (int i = 0; i < count; i++)
            {
                Person p = People[i];
                hashSet.Add(Metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable());
            }
#endif

            Dictionary<Person, IList<Vector3>> listDictionary = new(count, PersonComparer.Default);
            for (int i = 0; i < count; i++)
            {
                listDictionary.Add(People[i], Metrics[i].ToList());
            }

            GroupingSet<Person, Vector3> groupingSet = new(count, PersonComparer.Default);
            for (int i = 0; i < count; i++)
            {
                groupingSet.Add(People[i], Metrics[i]);
            }

            return (hashSet, listDictionary, groupingSet);
        }
    }
}
