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
    public class DictionaryTests
    {
        public (Person[] People, Vector3[][] Metrics) SampleData = Generator.GenerateSampleData(10, 20, new Random());

        public IDictionary<Person, IEnumerable<Vector3>> GenerateSampleData()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = new GroupingSet<Person, Vector3>();
            
            for(int i = 0; i < 10; i++)
                dic.Add(SampleData.People[i], SampleData.Metrics[i]);
            return dic;
        }

        public void TestAdd()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = new GroupingSet<Person, Vector3>();
            dic.Add(SampleData.People[0], SampleData.Metrics[0]);

            dic.Keys.Should().BeEquivalentTo(SampleData.People[0]);
            dic.Values.Should().BeEquivalentTo(SampleData.Metrics[0]);
            dic.Count.Should().Be(1);
            dic.GetEnumerator().MoveNext().Should().BeTrue();
            dic.GetEnumerator().MoveNext().Should().BeFalse();
        }

        public void TestClear()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();

            dic.Clear();

            dic.Keys.Should().BeEmpty();
            dic.Values.Should().BeEmpty();
            dic.Should().BeEmpty();
            dic.GetEnumerator().MoveNext().Should().BeFalse();
        }

        public void TestContainsKey()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();

            dic.ContainsKey(new Person()).Should().BeFalse();
            Assert.Throws<ArgumentNullException>(() => dic.ContainsKey(null));
            foreach(Person p in SampleData.People)
                dic.ContainsKey(p).Should().BeTrue();
        }

        public void TestTryGetValue()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();

            for (int i = 0; i < SampleData.People.Length; i++)
            {
                dic.TryGetValue(SampleData.People[i], out var metrics).Should().BeTrue();
                metrics.Should().BeEquivalentTo(SampleData.Metrics[i]);
            }
            dic.TryGetValue(new Person(), out _).Should().BeFalse();
            Assert.Throws<ArgumentNullException>(() => dic.TryGetValue(null, out _));
        }

        public void TestIndexer()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();

            for (int i = 0; i < SampleData.People.Length; i++)
            {
                dic[SampleData.People[i]].Should().BeEquivalentTo(SampleData.Metrics[i]);
            }
            Assert.Throws<KeyNotFoundException>(() => _ = dic[new Person()]);
            Assert.Throws<ArgumentNullException>(() => dic.TryGetValue(null, out _));
        }

        public void TestCopyTo()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            var array = new KeyValuePair<Person, IEnumerable<Vector3>>[dic.Count];
            dic.CopyTo(array, 0);
            for(int i = 0; i < array.Length; i++)
                array[i].Should().NotBeNull();
        }

        public void TestKeyCollection_Enumerator()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            ICollection<Person> keys = dic.Keys;
            keys.ToArray().Should().BeEquivalentTo(SampleData.People);
        }

        public void TestKeyCollection_Remove()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            ICollection<Person> keys = dic.Keys;

            keys.Remove(new Person()).Should().BeFalse();
            Assert.Throws<ArgumentNullException>(() => keys.Remove(null));
            foreach(Person p in SampleData.People)
                keys.Remove(p).Should().BeTrue();

            dic.Should().BeEmpty();
            keys.Should().BeEmpty();
        }

        public void TestKeyCollection_Contains()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            ICollection<Person> keys = dic.Keys;

            keys.Contains(new Person()).Should().BeFalse();
            Assert.Throws<ArgumentNullException>(() => keys.Contains(null));
            foreach(Person p in SampleData.People)
                keys.Contains(p).Should().BeTrue();
        }

        public void TestKeyCollection_Clear()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            ICollection<Person> keys = dic.Keys;

            keys.Clear();

            dic.Should().BeEmpty();
            keys.Should().BeEmpty();

            keys.Clear();

            dic.Should().BeEmpty();
            keys.Should().BeEmpty();
        }

        public void TestKeyCollection_CopyTo()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            var array = new Person[dic.Count];
            dic.Keys.CopyTo(array, 0);
            for(int i = 0; i < array.Length; i++)
                array[i].Should().NotBeNull();
        }

        public void TestValueCollection_Enumerator()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            ICollection<IEnumerable<Vector3>> values = dic.Values;
            values.ToArray().Should().BeEquivalentTo(SampleData.Metrics);
        }

        public void TestValueCollection_Remove()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            ICollection<IEnumerable<Vector3>> values = dic.Values;
            Assert.Throws<NotSupportedException>(() => values.Remove(Enumerable.Empty<Vector3>()));
        }

        public void TestValueCollection_Contains()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            ICollection<IEnumerable<Vector3>> values = dic.Values;
            Assert.Throws<NotSupportedException>(() => values.Contains(Enumerable.Empty<Vector3>()));
        }

        public void TestValueCollection_Clear()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            ICollection<Person> keys = dic.Keys;

            keys.Clear();

            dic.Should().BeEmpty();
            keys.Should().BeEmpty();

            keys.Clear();

            dic.Should().BeEmpty();
            keys.Should().BeEmpty();
        }

        public void TestValueCollection_CopyTo()
        {
            IDictionary<Person, IEnumerable<Vector3>> dic = GenerateSampleData();
            var array = new IEnumerable<Vector3>[dic.Count];
            dic.Values.CopyTo(array, 0);
            for(int i = 0; i < array.Length; i++)
                array[i].Should().NotBeEmpty();
        }
    }
}
