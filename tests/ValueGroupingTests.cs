using System;
using System.Linq;

using FluentAssertions;

using KeyValueCollection.Grouping;
using KeyValueCollection.Tests.Utility;

using NUnit.Framework;

namespace KeyValueCollection.Tests
{
    [TestFixture]
    public class ValueGroupingTests
    {
        [Test]
        public void TestEnumerator()
        {
            Person person = Generator.GetRandomPeople(1)[0];
            IGrouping<Person, double> personDoubleGroup = Generator.GetRandomNumbers(1100, (0, 40), new Random()).GroupBy(_ => person).First();
            Grouping<Person, double> grouping = Grouping<Person, double>.From(personDoubleGroup, PersonComparer.Default);
            grouping.Count.Should().Be(1100);
            grouping.Should().BeEquivalentTo(personDoubleGroup);
            grouping.ToArray().Should().BeEquivalentTo(personDoubleGroup);
        }
    }
}
