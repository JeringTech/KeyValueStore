using BenchmarkDotNet.Attributes;
using FASTER.core;
using MessagePack;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jering.KeyValueStore.Performance
{
    // TODO
    // - Benchmark different types of values
    //   - Primitive
    //   - Fixed length struct
    //   - Variable length struct
    //   - Class
    // - Benchmark different memory usage levels
    //   - Half in memory, half on disk
    //   - All in memory
    // - Benchmark delete operation
    [MemoryDiagnoser]
    public class LowMemoryUsageBenchmarks
    {
        private IMixedStorageKVStore<int, string> _mixedStorageKVStore;
        private MixedStorageKVStoreOptions _mixedStorageKVStoreOptions;
        private const int UPSERT_NUM_OPERATIONS = 1_000_000;
        private const int READ_NUM_OPERATIONS = 10_000;
        private string _dummyValue = "dummyString";
        private List<Task> _readTasks = new();

        // Concurrent inserts without compression
        [GlobalSetup(Target = nameof(Upsert_ConcurrentInserts_WithoutCompression))]
        public void Upsert_ConcurrentInserts_WithoutCompression_GlobalSetup()
        {
            _mixedStorageKVStoreOptions = new()
            {
                PageSizeBits = 12, // 4 KB
                MemorySizeBits = 13, // 2 pages
                MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
            };
        }

        [IterationSetup(Target = nameof(Upsert_ConcurrentInserts_WithoutCompression))]
        public void Upsert_ConcurrentInserts_WithoutCompression_IterationSetup()
        {
            //_mixedStorageKVStore = new ObjLogMixedStorageKVStore<int, string>(_mixedStorageKVStoreOptions);
            //_mixedStorageKVStore = new MemoryMixedStorageKVStore<int, string>(_mixedStorageKVStoreOptions);
            _mixedStorageKVStore = new MixedStorageKVStore<int, string>(_mixedStorageKVStoreOptions);
        }

        [Benchmark]
        public void Upsert_ConcurrentInserts_WithoutCompression()
        {
            Parallel.For(0, UPSERT_NUM_OPERATIONS, key => _mixedStorageKVStore.Upsert(key, _dummyValue));
        }

        [IterationCleanup(Target = nameof(Upsert_ConcurrentInserts_WithoutCompression))]
        public void Upsert_ConcurrentInserts_WithoutCompression_IterationCleanup()
        {
            _mixedStorageKVStore.Dispose();
        }

        // Concurrent reads without compression
        [GlobalSetup(Target = nameof(Upsert_ConcurrentReads_WithoutCompression))]
        public void Upsert_ConcurrentReads_WithoutCompressions_GlobalSetup()
        {
            _mixedStorageKVStoreOptions = new()
            {
                PageSizeBits = 12, // 4 KB
                MemorySizeBits = 13, // 2 pages
                MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
            };
            //_mixedStorageKVStore = new ObjLogMixedStorageKVStore<int, string>(_mixedStorageKVStoreOptions);
            //_mixedStorageKVStore = new MemoryMixedStorageKVStore<int, string>(_mixedStorageKVStoreOptions);
            _mixedStorageKVStore = new MixedStorageKVStore<int, string>(_mixedStorageKVStoreOptions);
            Parallel.For(0, READ_NUM_OPERATIONS, key => _mixedStorageKVStore.Upsert(key, _dummyValue));
        }

        [IterationSetup(Target = nameof(Upsert_ConcurrentReads_WithoutCompression))]
        public void Upsert_ConcurrentReads_WithoutCompressions_IterationSetup()
        {
            _readTasks.Clear();
        }

        [Benchmark]
        public async Task Upsert_ConcurrentReads_WithoutCompression()
        {
            for (int key = 0; key < READ_NUM_OPERATIONS; key++)
            {
                _readTasks.Add(ReadAsync(key));
            }
            await Task.WhenAll(_readTasks).ConfigureAwait(false);
        }

        private async Task<(Status, string?)> ReadAsync(int key)
        {
            await Task.Yield();

            return await _mixedStorageKVStore.ReadAsync(key).ConfigureAwait(false);
        }

        [GlobalCleanup(Target = nameof(Upsert_ConcurrentReads_WithoutCompression))]
        public void Upsert_ConcurrentReads_WithoutCompressions_GlobalCleanup()
        {
            _mixedStorageKVStore.Dispose();
        }
    }
}
