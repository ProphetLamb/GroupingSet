using System;
using System.Collections.Generic;
using System.Linq;

using FluentAssertions;

using KeyValueCollection.Extensions;
using KeyValueCollection.Tests.Utility;

using NUnit.Framework;

namespace KeyValueCollection.Tests
{
    [TestFixture]
    public class CtorTests
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
            Person[] keys = Generator.GetRandomPeople(4000);
            double[][] elements = Generator.GetRandomNumbersMatrix(4000, 200, (0, 40), new Random());
            GroupingSet<Person, double> set = new();
            for (int i = 0; i < 4000; i++)
            {
                set.Add(keys[i], elements[i]);
            }
            set.ShouldHaveKeysAndValues(keys, elements);
        }

        [Test]
        public void TestCtorEnumerable()
        {
            Person[] people = Generator.GetRandomPeople(4000);
            IEnumerable<string>[] lastNames = people.Select(p => new[]{ p.LastName }.AsEnumerable()).ToArray();

            Dictionary<Person, string> dic = people.ToDictionary(pair => pair, pair => pair.LastName);
            GroupingSet<Person, string> set = new(dic);
            GroupingSet<Person, string> other = dic.GroupBy(pair => pair.Key, pair => pair.Value).ToSet();
            set.ShouldHaveKeysAndValues(people, lastNames);
            other.ShouldHaveKeysAndValues(people,lastNames);
        }
    }
}
