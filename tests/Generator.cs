using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Bogus;

using FluentAssertions;

namespace KeyValueCollection.Tests
{
    public static class Generator
    {

        public static void ShouldHaveKeysAndValues<TKey, TElement>(this GroupingSet<TKey, TElement> set, TKey[] keys, IEnumerable<TElement>[] elements)
        {
            Debug.Assert(keys.Length == elements.Length);
            int count = keys.Length;
            set.Count.Should().Be(count);
            for(int i = 0; i < count; i++)
            {
                set[keys[i]].Should().BeEquivalentTo(elements[i]);
            }
            set.Keys.Should().BeEquivalentTo(keys);
            set.Values.Should().BeEquivalentTo(elements);
        }

        public static Person[] GetRandomPeople(int count)
        {
            var faker = new Faker<Person>()
                .RuleFor(p => p.FirstName, f => f.Name.FirstName())
                .RuleFor(p => p.LastName, f => f.Name.FirstName())
                .RuleFor(p => p.BirthDate, f => f.Date.Past())
                .RuleFor(p => p.BirthLocation, f => f.Lorem.Sentence());
            return faker.Generate(count).ToArray();
        }

        public static double[][] GetRandomNumbersMatrix(int rows, int cols, double minIncl, double maxExcl, Random random)
        {
            double[][] matrix = new double[rows][];
            for (int i = 0; i < rows; i++)
            {
                matrix[i] = GetRandomNumbers(cols, minIncl, maxExcl, random);
            }
            return matrix;
        }

        public static double[] GetRandomNumbers(int count, double minIncl, double maxExcl, Random random)
        {
            double[] array = new double[count];
            for (int i = 0; i < count; i++)
                array[i] = minIncl + random.NextDouble() * maxExcl;
            return array;
        }

        public static (string FirstNames, string LastNames)[] GetRandomNames(int count)
        {
            string[] keys = new Faker<string>().RuleFor(s => s, f => f.Name.FirstName()).Generate(count).ToArray();
            string[] values = new Faker<string>().RuleFor(s => s, f => f.Name.LastName()).Generate(count).ToArray();
            return keys.Zip(values).ToArray();
        }
    }
}
