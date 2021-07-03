using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

using FluentAssertions;

using KeyValueCollection.Extensions;
using KeyValueCollection.Tests;
using KeyValueCollection.Tests.Utility;

namespace KeyValueCollection.Benchmark
{
    [MemoryDiagnoser]
    [HardwareCounters(
        HardwareCounter.BranchMispredictions,
        HardwareCounter.BranchInstructions)]
    [SimpleJob(RuntimeMoniker.Net50)]
    public class InitBenchmark : BenchmarkBase
    {
        [Params(100,1000,10000)]
        public int Count;

        [Params(10,100)]
        public int VectorFieldSize;

        [GlobalSetup]
        public void Setup()
        {
            Random rng = new(12408782);
            (People, Metrics) = Generator.GenerateSampleData(Count, VectorFieldSize, rng);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            CleanupSampleData();
        }

        [IterationCleanup]
        public void CleanupIteration()
        {
            CleanupGeneratedSets();
        }

        [Benchmark]
        public void HashSet_Create_KnownSize()
        {
            HashSet = new HashSet<IGrouping<Person, Vector3>>(Count, GroupingComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                Person p = People[i];
                HashSet.Add(Metrics[i].GroupBy(_ => p, PersonComparer.Default).First());
            }
            HashSet.Count.Should().Be(Count);
            HashSet = null;
        }

        [Benchmark]
        public void Dictionary_Create_KnownSize()
        {
            ListDictionary = new Dictionary<Person, IList<Vector3>>(Count, PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                ListDictionary.Add(People[i], Metrics[i].ToList());
            }
            ListDictionary.Count.Should().Be(Count);
            ListDictionary = null;
        }

        [Benchmark]
        public void GroupingSet_Create_KnownSize()
        {
            GroupingSet = new GroupingSet<Person, Vector3>(Count, PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                GroupingSet.Add(People[i], Metrics[i]);
            }
            GroupingSet.Count.Should().Be(Count);
            GroupingSet = null;
        }

        [Benchmark]
        public void HashSet_Create_UnknownSize()
        {
            HashSet = new HashSet<IGrouping<Person, Vector3>>(GroupingComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                Person p = People[i];
                HashSet.Add(Metrics[i].GroupBy(_ => p, PersonComparer.Default).First());
            }
            HashSet.Count.Should().Be(Count);
            HashSet = null;
        }

        [Benchmark]
        public void Dictionary_Create_UnknownSize()
        {
            ListDictionary = new Dictionary<Person, IList<Vector3>>(PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                ListDictionary.Add(People[i], Metrics[i].ToList());
            }
            ListDictionary.Count.Should().Be(Count);
            ListDictionary = null;
        }

        [Benchmark]
        public void GroupingSet_Create_UnknownSize()
        {
            GroupingSet = new GroupingSet<Person, Vector3>(PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                GroupingSet.Add(People[i], Metrics[i]);
            }
            GroupingSet.Count.Should().Be(Count);
            GroupingSet = null;
        }
    }
}
