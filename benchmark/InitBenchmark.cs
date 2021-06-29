using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using FluentAssertions;

using GenericRange;

using KeyValueCollection.Extensions;
using KeyValueCollection.Tests;
using KeyValueCollection.Tests.Utility;

namespace KeyValueCollection.Benchmark
{
    [SimpleJob(RuntimeMoniker.Net50)]
    public class InitBenchmark
    {
        private Person[] _people;
        private Vector3[][] _metrics;

        [Params(100,1000,10000)]
        public int Count;

        [Params(10,100)]
        public int VectorFieldSize;

        public HashSet<IGrouping<Person, Vector3>> HashSet;

        public GroupingSet<Person, Vector3> GroupingSet;

        [GlobalSetup]
        public void Setup()
        {
            _people = Generator.GetRandomPeople(Count);
            
            var rng = new Random(12408782);
            Range<float> range = new(-3.14f, 3.14f);
            _metrics = new Vector3[Count][];
            for (int i = 0; i < Count; i++)
                _metrics[i] = Generator.GetRandomVector3s(VectorFieldSize, range, range, range, rng);
        }
        
        [Benchmark]
        public void HashSet_Create_KnownSize()
        {
            HashSet = new HashSet<IGrouping<Person, Vector3>>(Count, GroupingComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                Person p = _people[i];
                HashSet.Add(_metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable());
            }
            HashSet.Count.Should().Be(Count);
        }

        [Benchmark]
        public void GroupingSet_Create_KnownSize()
        {
            GroupingSet = new GroupingSet<Person, Vector3>(Count, PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                GroupingSet.Add(_people[i], _metrics[i]);
            }
            GroupingSet.Count.Should().Be(Count);
        }
        
        [Benchmark]
        public void HashSet_Create_UnknownSize()
        {
            HashSet = new HashSet<IGrouping<Person, Vector3>>(GroupingComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                Person p = _people[i];
                HashSet.Add(_metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable());
            }
            HashSet.Count.Should().Be(Count);
        }

        [Benchmark]
        public void GroupingSet_Create_UnknownSize()
        {
            GroupingSet = new GroupingSet<Person, Vector3>(PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                GroupingSet.Add(_people[i], _metrics[i]);
            }
            GroupingSet.Count.Should().Be(Count);
        }
    }
}
