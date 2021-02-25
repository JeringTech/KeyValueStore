using BenchmarkDotNet.Attributes;
using System.Threading.Tasks;

namespace Jering.KeyValueStore.Performance
{
    // TODO
    // - Benchmark different values types
    //   - Primitive
    //   - Fixed length struct
    //   - Variable length struct
    //   - Class
    // - Benchmark different memory usage levels
    //   - Half in memory, half on disk
    //   - All in memory
    [MemoryDiagnoser]
    public class LowMemoryUsageBenchmarks
    {
        private MixedStorageKeyValueStore<int, string> _mixedStorageKeyValueStore;
        private MixedStorageKeyValueStoreOptions _mixedStorageKeyValueStoreOptions = new MixedStorageKeyValueStoreOptions()
        {
            PageSizeBits = 12, // 4 KB
            MemorySizeBits = 13 // 2 pages
        };
        private int _numOperations = 1_000_000;
        private string _dummyValue = "dummyString";

        // Concurrent inserts
        [IterationSetup(Target = nameof(Upsert_ConcurrentInserts))]
        public void Upsert_ConcurrentInserts_IterationSetup()
        {
            _mixedStorageKeyValueStore = new MixedStorageKeyValueStore<int, string>(_mixedStorageKeyValueStoreOptions);
        }

        [Benchmark]
        public void Upsert_ConcurrentInserts()
        {
            Parallel.For(0, 1_000_000, UpsertAction);
        }

        private void UpsertAction(int key)
        {
            _mixedStorageKeyValueStore.Upsert(key, _dummyValue);
        }

        [IterationCleanup(Target = nameof(Upsert_ConcurrentInserts))]
        public void Upsert_ConcurrentInserts_IterationCleanup()
        {
            _mixedStorageKeyValueStore.Dispose();
        }

        // Concurrent reads
        [GlobalSetup(Target = nameof(Upsert_ConcurrentReads))]
        public void Upsert_ConcurrentReads_GlobalSetup()
        {
            _mixedStorageKeyValueStore = new MixedStorageKeyValueStore<int, string>(_mixedStorageKeyValueStoreOptions);
            Parallel.For(0, _numOperations, UpsertAction);
        }

        [Benchmark]
        public void Upsert_ConcurrentReads()
        {
            Parallel.For(0, _numOperations, ReadAction);
        }

        private async void ReadAction(int key)
        {
            await _mixedStorageKeyValueStore.ReadAsync(key).ConfigureAwait(false);
        }

        [GlobalCleanup(Target = nameof(Upsert_ConcurrentReads))]
        public void Upsert_ConcurrentReads_GlobalCleanup()
        {
            _mixedStorageKeyValueStore.Dispose();
        }
    }
}
