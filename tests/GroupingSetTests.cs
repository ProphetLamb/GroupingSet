using NUnit.Framework;

namespace KeyValueCollection.Tests
{
    [TestFixture]
    public class GroupSetTests
    {
        [Test]
        public void Test()
        {
            GroupingSet<string, double> gSet = new() {
                { "one", new[] { 1.0, 1.1, 1.2 } },
                { "two", new[] { 2.0, 2.1, 2.2 } }
            };
        }
    }
}
