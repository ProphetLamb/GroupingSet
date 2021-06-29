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
        
        [Params(100,1000,10000)]
        public int Count;

        [Params(10,100)]
        public int VectorFieldSize;

        public HashSet<IGrouping<Person, Vector3>> HashSet;

        public GroupingSet<Person, Vector3> GroupingSet;

        [GlobalSetup]
        public void Setup()
        {
            var people = Generator.GetRandomPeople(Count);
            
            var rng = new Random(12408782);
            Range<float> range = new(-3.14f, 3.14f);
            var metrics = new Vector3[Count][];
            for (int i = 0; i < Count; i++)
                metrics[i] = Generator.GetRandomVector3s(VectorFieldSize, range, range, range, rng);
            
            HashSet = new HashSet<IGrouping<Person, Vector3>>(Count, GroupingComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                Person p = people[i];
                HashSet.Add(metrics[i].GroupBy(_ => p, PersonComparer.Default).First().ToImmutable());
            }
            
            GroupingSet = new GroupingSet<Person, Vector3>(Count, PersonComparer.Default);
            for (int i = 0; i < Count; i++)
            {
                GroupingSet.Add(people[i], metrics[i]);
            }

            _people = people.OrderBy(_ => rng.Next()).ToArray();
        }


        [Benchmark]
        public void HashSet_Access()
        {
            foreach (Person p in _people)
            {
                IGrouping<Person, Vector3> grouping = GetDummyGrouping(p);
                HashSet.TryGetValue(grouping, out grouping);
                grouping!.Count().Should().Be(VectorFieldSize);
            }
        }

        [Benchmark]
        public void GroupingSet_Access()
        {
            foreach (Person p in _people)
            {
                ValueGrouping<Person, Vector3> grouping = GroupingSet[p];
                grouping.Count.Should().Be(VectorFieldSize);
            }
        }

        private static readonly Vector3[] _vector3s = new[]{ new Vector3() };
        public IGrouping<Person, Vector3> GetDummyGrouping(Person person)
        {
            return _vector3s.GroupBy(_ => person).First();
        }
    }
}
