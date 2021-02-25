using BenchmarkDotNet.Running;

namespace Jering.KeyValueStore.Performance
{
    class Program
    {
        static void Main(string[] args)
        {
            //BenchmarkRunner.Run<LowMemoryUsageBenchmarks>();
            BenchmarkRunner.Run<NormalMemoryUsageBenchmarks>();
        }
    }
}
