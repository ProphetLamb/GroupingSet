using System.Threading;

using BenchmarkDotNet.Running;

namespace KeyValueCollection.Benchmark
{
    public class Benchmark
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<AccessBenchmark>();
            //BenchmarkRunner.Run<InitBenchmark>();
        }
    }
}
