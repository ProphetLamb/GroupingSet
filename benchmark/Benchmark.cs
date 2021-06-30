using BenchmarkDotNet.Running;

namespace KeyValueCollection.Benchmark
{
    public static class Benchmark
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<MutateBenchmark>();
            //BenchmarkRunner.Run<AccessBenchmark>();
            //BenchmarkRunner.Run<InitBenchmark>();
        }
    }
}
