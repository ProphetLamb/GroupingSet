using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using FluentAssertions;

using KeyValueCollection.Grouping;
using KeyValueCollection.Tests;

namespace KeyValueCollection.Benchmark
{
    [SimpleJob(RuntimeMoniker.Net50)]
    public class AccessBenchmark : BenchmarkBase
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
            (HashSet, ListDictionary, GroupingSet) = GenerateSetsFromData(Count, VectorFieldSize);
            People = People.OrderBy(_ => rng.Next()).ToArray();
        }

#if BENCH_HASHSET
        [Benchmark]
        public void HashSet_Access()
        {
            for (var i = 0; i < People.Length; i++)
            {
                Person p = People[i];
                IGrouping<Person, Vector3> grouping = Metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable();
                HashSet.TryGetValue(grouping, out grouping);
                grouping!.Count().Should().Be(VectorFieldSize);
            }
        }
#endif

        [Benchmark]
        public void EnumerableDictionary_Access()
        {
            foreach (Person p in People)
            {
                IEnumerable<Vector3> vectors = ListDictionary[p];
                vectors.Count().Should().Be(VectorFieldSize);
            }
        }

        [Benchmark]
        public void GroupingSet_Access()
        {
            foreach (Person p in People)
            {
                ref ValueGrouping<Person, Vector3> grouping = ref GroupingSet[p];
                grouping.Count.Should().Be(VectorFieldSize);
            }
        }
    }
}
