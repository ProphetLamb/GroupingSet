using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace KeyValueCollection.Tests
{
    public class Person
    {
        public Person()
        {

        }

        public Person(string firstName, string lastName, DateTimeOffset birthDate, string birthLocation)
        {
            FirstName = firstName;
            LastName = lastName;
            BirthDate = birthDate;
            BirthLocation = birthLocation;
        }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTimeOffset BirthDate { get; set; }
        public string BirthLocation { get; set; }
    }

    public sealed class PersonComparer : IEqualityComparer<Person>
    {
        private static readonly Lazy<PersonComparer> s_default = new(() => new PersonComparer());

        public static PersonComparer Default => s_default.Value!;

        public bool Equals(Person x, Person y)
        {
            return x.FirstName.Equals(y.FirstName)
                && x.LastName.Equals(y.LastName)
                && x.BirthDate.EqualsExact(y.BirthDate)
                && x.BirthLocation.Equals(y.BirthLocation);
        }

        public int GetHashCode([DisallowNull] Person obj)
        {
            return HashCode.Combine(obj.FirstName, obj.LastName);
        }
    }
}
