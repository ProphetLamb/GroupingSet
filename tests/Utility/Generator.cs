using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

using Bogus;

using FluentAssertions;

using GenericRange;
using GenericRange.Extensions;

namespace KeyValueCollection.Tests.Utility
{
    public static class Generator
    {
        private static readonly Range<double> _zeroOneD = new(0, 1);
        private static readonly Range<float> _zeroOneF = new(0, 1);
        
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

        public static double[][] GetRandomNumbersMatrix(int rows, int cols, Range<double> range, Random random)
        {
            double[][] matrix = new double[rows][];
            for (int i = 0; i < rows; i++)
            {
                matrix[i] = GetRandomNumbers(cols, range, random);
            }
            return matrix;
        }

        public static double[] GetRandomNumbers(int count, Range<double> range, Random random)
        {
            double[] array = new double[count];
            for (int i = 0; i < count; i++)
                array[i] = _zeroOneD.Map(range, random.NextDouble());
            return array;
        }

        public static (string FirstNames, string LastNames)[] GetRandomNames(int count)
        {
            string[] keys = new Faker<string>().RuleFor(s => s, f => f.Name.FirstName()).Generate(count).ToArray();
            string[] values = new Faker<string>().RuleFor(s => s, f => f.Name.LastName()).Generate(count).ToArray();
            return keys.Zip(values).ToArray();
        }

        public static Vector3[] GetRandomVector3s(int count, Range<float> xRange, Range<float> yRange, Range<float> zRange, Random random)
        {
            Vector3[] array = new Vector3[count];
            for (int i = 0; i < count; i++)
                array[i] = GetRandomVector3(xRange, yRange, zRange, random);
            return array;
        }
        
        public static Vector3 GetRandomVector3(Range<float> xRange, Range<float> yRange, Range<float> zRange, Random random)
        {
            return new(_zeroOneF.Map(xRange, (float)random.NextDouble()), _zeroOneF.Map(yRange, (float)random.NextDouble()), _zeroOneF.Map(zRange, (float)random.NextDouble()));
        }
    }
}
