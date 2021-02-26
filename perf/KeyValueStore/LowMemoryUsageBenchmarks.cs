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
        private MixedStorageKeyValueStore<int, string> _mixedStorageKeyValueStore;
        private MixedStorageKeyValueStoreOptions _mixedStorageKeyValueStoreOptions;
        private const int UPSERT_NUM_OPERATIONS = 1_000_000;
        private const int READ_NUM_OPERATIONS = 10_000;
        private string _dummyValue = "dummyString";
        private List<Task> _readTasks = new();

        // Concurrent inserts without compression
        [GlobalSetup(Target = nameof(Upsert_ConcurrentInserts_WithoutCompression))]
        public void Upsert_ConcurrentInserts_WithoutCompression_GlobalSetup()
        {
            _mixedStorageKeyValueStoreOptions = new()
            {
                PageSizeBits = 12, // 4 KB
                MemorySizeBits = 13, // 2 pages
                MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
            };
        }

        [IterationSetup(Target = nameof(Upsert_ConcurrentInserts_WithoutCompression))]
        public void Upsert_ConcurrentInserts_WithoutCompression_IterationSetup()
        {
            _mixedStorageKeyValueStore = new MixedStorageKeyValueStore<int, string>(_mixedStorageKeyValueStoreOptions);
        }

        [Benchmark]
        public void Upsert_ConcurrentInserts_WithoutCompression()
        {
            Parallel.For(0, UPSERT_NUM_OPERATIONS, key => _mixedStorageKeyValueStore.Upsert(key, _dummyValue));
        }

        [IterationCleanup(Target = nameof(Upsert_ConcurrentInserts_WithoutCompression))]
        public void Upsert_ConcurrentInserts_WithoutCompression_IterationCleanup()
        {
            _mixedStorageKeyValueStore.Dispose();
        }

        // Concurrent inserts with compression
        [GlobalSetup(Target = nameof(Upsert_ConcurrentInserts_WithCompression))]
        public void Upsert_ConcurrentInserts_WithCompression_GlobalSetup()
        {
            _mixedStorageKeyValueStoreOptions = new()
            {
                PageSizeBits = 12, // 4 KB
                MemorySizeBits = 13, // 2 pages
            };
        }

        [IterationSetup(Target = nameof(Upsert_ConcurrentInserts_WithCompression))]
        public void Upsert_ConcurrentInserts_WithCompression_IterationSetup()
        {
            _mixedStorageKeyValueStore = new MixedStorageKeyValueStore<int, string>(_mixedStorageKeyValueStoreOptions);
        }

        [Benchmark]
        public void Upsert_ConcurrentInserts_WithCompression()
        {
            Parallel.For(0, UPSERT_NUM_OPERATIONS, key => _mixedStorageKeyValueStore.Upsert(key, _dummyValue));
        }

        [IterationCleanup(Target = nameof(Upsert_ConcurrentInserts_WithCompression))]
        public void Upsert_ConcurrentInserts_WithCompression_IterationCleanup()
        {
            _mixedStorageKeyValueStore.Dispose();
        }

        // Concurrent reads without compression
        [GlobalSetup(Target = nameof(Upsert_ConcurrentReads_WithoutCompression))]
        public void Upsert_ConcurrentReads_WithoutCompressions_GlobalSetup()
        {
            _mixedStorageKeyValueStoreOptions = new()
            {
                PageSizeBits = 12, // 4 KB
                MemorySizeBits = 13, // 2 pages
                MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
            };
            _mixedStorageKeyValueStore = new MixedStorageKeyValueStore<int, string>(_mixedStorageKeyValueStoreOptions);
            Parallel.For(0, READ_NUM_OPERATIONS, key => _mixedStorageKeyValueStore.Upsert(key, _dummyValue));
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

        private async Task<(Status, string)> ReadAsync(int key)
        {
            await Task.Yield();

            return await _mixedStorageKeyValueStore.ReadAsync(key).ConfigureAwait(false);
        }

        [GlobalCleanup(Target = nameof(Upsert_ConcurrentReads_WithoutCompression))]
        public void Upsert_ConcurrentReads_WithoutCompressions_GlobalCleanup()
        {
            _mixedStorageKeyValueStore.Dispose();
        }
    }
}
