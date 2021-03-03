using BenchmarkDotNet.Running;

namespace Jering.KeyValueStore.Performance
{
    class Program
    {
        static void Main()
        {
            BenchmarkRunner.Run<LowMemoryUsageBenchmarks>();
        }
    }
}
