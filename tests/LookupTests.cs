using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FluentAssertions;
using KeyValueCollection.Tests.Utility;
using NUnit.Framework;

namespace KeyValueCollection.Tests
{
    [TestFixture]
    public class LookupTests
    {
        public (Person[] People, Vector3[][] Metrics) SampleData = Generator.GenerateSampleData(10, 20, new Random());

        public ILookup<Person, Vector3> GenerateSampleData()
        {
            GroupingSet<Person, Vector3> dic = new();
            for(int i = 0; i < 10; i++)
                dic.Add(SampleData.People[i], SampleData.Metrics[i]);
            return dic;
        }

        [Test]
        public void TestIndexer()
        {
            ILookup<Person, Vector3> dic = GenerateSampleData();
            for (int i = 0; i < SampleData.People.Length; i++)
                dic[SampleData.People[i]].Should().BeEquivalentTo(SampleData.Metrics[i]);

            Assert.Throws<KeyNotFoundException>(() => _ = dic[new Person()]);
            Assert.Throws<NullReferenceException>(() => _ = dic[null]);
        }

        [Test]
        public void TestContains()
        {
            ILookup<Person, Vector3> dic = GenerateSampleData();

            foreach(Person p in SampleData.People)
                dic.Contains(p).Should().BeTrue();

            dic.Contains(new Person()).Should().BeFalse();
            Assert.Throws<NullReferenceException>(() => dic.Contains(null));
        }
    }
}
