using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;

using FluentAssertions;
using GenericRange;
using KeyValueCollection.Extensions;
using KeyValueCollection.Tests.Utility;
using NUnit.Framework;

using BindingFlags = System.Reflection.BindingFlags;

namespace KeyValueCollection.Tests
{
    [TestFixture]
    public class GoupingSetTests
    {
        [Test]
        public void TestCtorNoParams()
        {
            GroupingSet<Person, double> set = new();
            set.Count.Should().Be(0);
            set.GetEnumerator().MoveNext().Should().BeFalse();
            set.Keys.Count.Should().Be(set.Values.Count).And.Be(0);
            set.Keys.GetEnumerator().MoveNext().Should().BeFalse();
            set.Values.GetEnumerator().MoveNext().Should().BeFalse();
        }

        [Test]
        public void TestCtorCapacity()
        {
            GroupingSet<Person, double> set = new(32);
            set.Count.Should().Be(0);
            set.GetEnumerator().MoveNext().Should().BeFalse();
            set.Keys.Count.Should().Be(set.Values.Count).And.Be(0);
            set.Keys.GetEnumerator().MoveNext().Should().BeFalse();
            set.Values.GetEnumerator().MoveNext().Should().BeFalse();
        }

        [Test]
        public void TestCtorCollection()
        {
            Person[] keys = Generator.GetRandomPeople(100);
            double[][] elements = Generator.GetRandomNumbersMatrix(100, 200, (0, 40), new Random());
            GroupingSet<Person, double> set = new();
            for (int i = 0; i < 100; i++)
            {
                set.Add(keys[i], elements[i]);
            }
            set.ShouldHaveKeysAndValues(keys, elements);
        }

        [Test]
        public void TestCtorEnumerable()
        {
            Person[] people = Generator.GetRandomPeople(100);
            IEnumerable<string>[] names = people.Select(p => new[] { p.LastName }.AsEnumerable()).ToArray();

            Dictionary<Person, string> dic = people.ToDictionary(pair => pair, pair => pair.LastName);
            GroupingSet<Person, string> set = new(dic);
            GroupingSet<Person, string> other = dic.GroupBy(pair => pair.Key, pair => pair.Value).ToSet();
            set.ShouldHaveKeysAndValues(people, names);
            other.ShouldHaveKeysAndValues(people, names);
        }
        [Test]
        public void TestCtorComparer()
        {
            GroupingSet<Person, double> set = new(PersonComparer.Default);
            set.Count.Should().Be(0);
            set.GetEnumerator().MoveNext().Should().BeFalse();
            set.Keys.Count.Should().Be(set.Values.Count).And.Be(0);
            set.Keys.GetEnumerator().MoveNext().Should().BeFalse();
            set.Values.GetEnumerator().MoveNext().Should().BeFalse();
        }

        [Test]
        public void TestCtorCapacityComparer()
        {
            GroupingSet<Person, double> set = new(32, PersonComparer.Default);
            set.Count.Should().Be(0);
            set.GetEnumerator().MoveNext().Should().BeFalse();
            set.Keys.Count.Should().Be(set.Values.Count).And.Be(0);
            set.Keys.GetEnumerator().MoveNext().Should().BeFalse();
            set.Values.GetEnumerator().MoveNext().Should().BeFalse();
        }

        [Test]
        public void TestCtorCollectionComparer()
        {
            Person[] keys = Generator.GetRandomPeople(100);
            double[][] elements = Generator.GetRandomNumbersMatrix(100,  200, new Range<double>(0, 40), new Random());
            GroupingSet<Person, double> set = new(PersonComparer.Default);
            for (int i = 0; i < 100; i++)
            {
                set.Add(keys[i], elements[i]);
            }
            set.ShouldHaveKeysAndValues(keys, elements);
        }

        [Test]
        public void TestCtorEnumerableComparer()
        {
            Person[] people = Generator.GetRandomPeople(100);
            IEnumerable<string>[] names = people.Select(p => new[] { p.LastName }.AsEnumerable()).ToArray();

            Dictionary<Person, string> dic = people.ToDictionary(pair => pair, pair => pair.LastName);
            GroupingSet<Person, string> set = new(dic, PersonComparer.Default);
            GroupingSet<Person, string> other = dic.GroupBy(pair => pair.Key, pair => pair.Value, PersonComparer.Default).ToSet(PersonComparer.Default);
            set.ShouldHaveKeysAndValues(people, names);
            other.ShouldHaveKeysAndValues(people,names);
        }

        [Test]
        public void TestAddIfNotExists()
        {
            Person[] people = Generator.GetRandomPeople(100);
            IEnumerable<string>[] names = people.Select(p => new[] { p.FirstName + p.LastName, p.FirstName, p.LastName }.AsEnumerable()).ToArray();

            GroupingSet<Person, string> set = new(PersonComparer.Default);
            for (int i = 0; i < 50; i++)
            {
                set.AddIfNotExists(people[i], names[i]).Should().Be(3);
            }

            for (int i = 50; i < 50; i++)
            {
                set.AddIfNotExists(people[i], names[i].First()).Should().BeTrue();
            }

            for (int i = 0; i < 50; i++)
            {
                set.AddIfNotExists(people[i], names[i]).Should().Be(-1);
            }

            for (int i = 50; i < 50; i++)
            {
                set.AddIfNotExists(people[i], names[i].First()).Should().BeFalse();
            }
        }

        [Test]
        public void TestCopyTo()
        {
            Person[] people = Generator.GetRandomPeople(100);
            IEnumerable<string>[] names = people.Select(p => new[] { p.FirstName + p.LastName, p.FirstName, p.LastName }.AsEnumerable()).ToArray();

            GroupingSet<Person, string> set = new(PersonComparer.Default);
            for (int i = 0; i < 100; i++)
                set.Add(people[i], names[i]);
            
            IGrouping<Person, string>[] array = new IGrouping<Person, string>[100];
            set.CopyTo(array);

            for (int i = 0; i < 100; i++)
                array[i].Should().NotBeNullOrEmpty();
        }

        [Test]
        public void TestAddRemove()
        {
            Random rng = new();
            (Person[] people, Vector3[][] metrics) = Generator.GenerateSampleData(100, 40, rng);

            List<Person> keyShadowList = new();
            GroupingSet<Person, Vector3[]> set = new(PersonComparer.Default);
            
            for (int i = 0; i < 100; i++)
            {
                if (rng.Next(Int32.MinValue, Int32.MaxValue) < 0)
                {
                    set.Remove(people[i]);
                    keyShadowList.Remove(people[i]);
                }
                else
                {
                    set.Add(people[i], metrics[i]);
                    keyShadowList.Add(people[i]);
                }
            }

            set.Keys.Should().BeEquivalentTo(keyShadowList);
        }
    }
}
