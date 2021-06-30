using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using FluentAssertions;

using KeyValueCollection.Extensions;
using KeyValueCollection.Tests;
using KeyValueCollection.Tests.Utility;

namespace KeyValueCollection.Benchmark
{
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
            (People, Metrics) = GenerateSampleData(Count, VectorFieldSize, rng);
        }

        [Benchmark]
        public void HashSet_Create_KnownSize()
        {
            HashSet = new HashSet<IGrouping<Person, Vector3>>(Count, GroupingComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                Person p = People[i];
                HashSet.Add(Metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable());
            }
            HashSet.Count.Should().Be(Count);
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
        }

        [Benchmark]
        public void HashSet_Create_UnknownSize()
        {
            HashSet = new HashSet<IGrouping<Person, Vector3>>(GroupingComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                Person p = People[i];
                HashSet.Add(Metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable());
            }
            HashSet.Count.Should().Be(Count);
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
        }
    }
}
