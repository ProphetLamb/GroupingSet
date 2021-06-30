using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using FluentAssertions;

using GenericRange;

using KeyValueCollection.Extensions;
using KeyValueCollection.Grouping;
using KeyValueCollection.Tests;
using KeyValueCollection.Tests.Utility;

namespace KeyValueCollection.Benchmark
{
    [SimpleJob(RuntimeMoniker.Net50)]
    public class AccessBenchmark
    {
        private Person[] _people;
        private Vector3[][] _metrics;
        
        [Params(100,1000,10000)]
        public int Count;

        [Params(10,100)]
        public int VectorFieldSize;

        public HashSet<IGrouping<Person, Vector3>> HashSet;

        public Dictionary<Person, IEnumerable<Vector3>> EnumerableDictionary;

        public GroupingSet<Person, Vector3> GroupingSet;

        [GlobalSetup]
        public void Setup()
        {
            var people = Generator.GetRandomPeople(Count);
            
            Random rng = new(12408782);
            Range<float> range = new(-3.14f, 3.14f);
            _metrics = new Vector3[Count][];
            for (int i = 0; i < Count; i++)
                _metrics[i] = Generator.GetRandomVector3s(VectorFieldSize, range, range, range, rng);
            
            HashSet = new HashSet<IGrouping<Person, Vector3>>(Count, GroupingComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                Person p = people[i];
                HashSet.Add(_metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable());
            }
            
            GroupingSet = new GroupingSet<Person, Vector3>(Count, PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                GroupingSet.Add(people[i], _metrics[i]);
            }
            
            EnumerableDictionary = new Dictionary<Person, IEnumerable<Vector3>>(Count, PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                EnumerableDictionary.Add(people[i], _metrics[i]);
            }

            _people = people.OrderBy(_ => rng.Next()).ToArray();
        }


        [Benchmark]
        public void HashSet_Access()
        {
            for (var i = 0; i < _people.Length; i++)
            {
                Person p = _people[i];
                IGrouping<Person, Vector3> grouping = _metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable();
                HashSet.TryGetValue(grouping, out grouping);
                grouping!.Count().Should().Be(VectorFieldSize);
            }
        }

        [Benchmark]
        public void EnumerableDictionary_Access()
        {
            foreach (Person p in _people)
            {
                IEnumerable<Vector3> vectors = EnumerableDictionary[p];
                vectors.Count().Should().Be(VectorFieldSize);
            }
        }

        [Benchmark]
        public void GroupingSet_Access()
        {
            foreach (Person p in _people)
            {
                ref ValueGrouping<Person, Vector3> grouping = ref GroupingSet[p];
                grouping.Count.Should().Be(VectorFieldSize);
            }
        }
    }
}
