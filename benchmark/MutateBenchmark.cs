using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using KeyValueCollection.Grouping;
using KeyValueCollection.Tests;
using KeyValueCollection.Tests.Utility;

namespace KeyValueCollection.Benchmark
{
    [MemoryDiagnoser]
    [HardwareCounters(
        HardwareCounter.BranchMispredictions,
        HardwareCounter.BranchInstructions)]
    [SimpleJob(RuntimeMoniker.Net50)]
    public class MutateBenchmark : BenchmarkBase
    {
        public Person[] MutPeople;
        public Vector3[][] MutMetrics;

        [Params(10, 100)]
        public int Count;

        [Params(10)]
        public int VectorFieldSize;
        
        [GlobalSetup]
        public void Setup()
        {
            Random rng = new(12408782);
            (People, Metrics) = Generator.GenerateSampleData(1000, 1, rng);
            (HashSet, ListDictionary, GroupingSet) = GenerateSetsFromData(1000);

            (MutPeople, MutMetrics) = Generator.GenerateSampleData(Count, VectorFieldSize, rng);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            CleanupSampleData();
            CleanupGeneratedSets();
        }

#if BENCH_HASHSET
        [Benchmark]
        public void HashSet_Add()
        {
            for (int i = 0; i < Count; i++)
            {
                Person p = MutPeople[i];
                IGrouping<Person, Vector3> g = HashSet.FirstOrDefault(g => PersonComparer.Default.Equals(g.Key, p));
                if (g == null)
                {
                    HashSet.Add(MutMetrics[i].GroupBy(_ => p).First());
                }
                else
                {
                    HashSet.Remove(g);
                    List<Vector3> newMetrics = new(MutMetrics[i]);
                    newMetrics.AddRange(g);
                    HashSet.Add(newMetrics.GroupBy(_ => p).First());
                }
            }
        }
#endif

        [Benchmark]
        public void Dictionary_Add()
        {
            for (int i = 0; i < Count; i++)
            {
                Person p = MutPeople[i];
                if (ListDictionary.TryGetValue(p, out IList<Vector3> list))
                {
                    foreach (var item in MutMetrics[i])
                        list.Add(item);
                }
                else
                {
                    ListDictionary.Add(p, MutMetrics[i].ToList());
                }
            }
        }

        [Benchmark]
        public void GroupingSet_Add()
        {
            for (int i = 0; i < Count; i++)
            {
                GroupingSet.Add(MutPeople[i], MutMetrics[i]);
            }
        }

#if BENCH_HASHSET
        [Benchmark]
        public void HashSet_Remove()
        {
            for (int i = 0; i < Count; i++)
            {
                Person p = MutPeople[i];
                IGrouping<Person, Vector3> g = HashSet.FirstOrDefault(g => PersonComparer.Default.Equals(g.Key, p));
                if (g != null)
                {
                    List<Vector3> metrics = new(g);
                    foreach(Vector3 vec in MutMetrics[i])
                        metrics.Remove(vec);
                    HashSet.Remove(g);
                    if (metrics.Count != 0)
                        HashSet.Add(metrics.GroupBy(_ => p).First());
                }
            }
        }
#endif

        [Benchmark]
        public void Dictionary_Remove()
        {
            for (int i = 0; i < Count; i++)
            {
                Person p = MutPeople[i];
                if (ListDictionary.TryGetValue(p, out IList<Vector3> metrics))
                {
                    foreach(Vector3 vec in MutMetrics[i])
                        metrics.Remove(vec);
                    if (metrics.Count == 0)
                        ListDictionary.Remove(p);
                }
            }
        }

        [Benchmark]
        public void GroupingSet_Contains_Remove()
        {
            for (int i = 0; i < Count; i++)
            {
                Person p = MutPeople[i];
                if (GroupingSet.ContainsKey(p))
                {
                    Grouping<Person, Vector3> grouping = GroupingSet[p];
                    foreach(Vector3 vec in MutMetrics[i])
                        grouping.Remove(vec);
                    if (grouping.Count == 0)
                        GroupingSet.Remove(p);
                }
            }
        }

        [Benchmark]
        public void GroupingSet_TryGet_Remove()
        {
            for (int i = 0; i < Count; i++)
            {
                Person p = MutPeople[i];
                if (GroupingSet.TryGetValue(p, out Grouping<Person, Vector3> grouping))
                {
                    foreach(Vector3 vec in MutMetrics[i])
                        grouping.Remove(vec);
                    if (grouping.Count == 0)
                        GroupingSet.Remove(p);
                }
            }
        }
    }
}
