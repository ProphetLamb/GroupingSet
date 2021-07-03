using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

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

        public (HashSet<IGrouping<Person, Vector3>> HashSet, Dictionary<Person, IList<Vector3>> Dictionary, GroupingSet<Person, Vector3> GroupingSet) GenerateSetsFromData(int count)
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

        public void CleanupGeneratedSets()
        {
            WeakReference hashset = new(HashSet);
            WeakReference dictionary = new(ListDictionary);
            WeakReference set = new(GroupingSet);
            HashSet = null;
            ListDictionary = null;
            GroupingSet = null;
            while (hashset.IsAlive || dictionary.IsAlive || set.IsAlive)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        
        public void CleanupSampleData()
        {
            WeakReference people = new(People);
            WeakReference metrics = new(Metrics);
            People = null;
            Metrics = null;
            while (people.IsAlive || metrics.IsAlive)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
