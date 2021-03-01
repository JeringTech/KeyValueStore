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
#pragma warning disable CS8618
        private IMixedStorageKVStore<int, DummyClass> _mixedStorageKVStore;
        private MixedStorageKVStoreOptions _mixedStorageKVStoreOptions;
#pragma warning restore CS8618
        private const int UPSERT_NUM_OPERATIONS = 350_000;
        private const int READ_NUM_OPERATIONS = 10_000;
        private string _dummyValue = "dummyString";
        private readonly List<Task> _readTasks = new();
        private readonly DummyClass _dummyClassInstance = new()
        {
            // Populate with dummy values
            DummyString = "dummyString",
            DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
            DummyInt = 10,
            DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
        };

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
            _mixedStorageKVStore = new MixedStorageKVStore<int, DummyClass>(_mixedStorageKVStoreOptions);
        }

        [Benchmark]
        public void Upsert_ConcurrentInserts_WithoutCompression()
        {
            Parallel.For(0, UPSERT_NUM_OPERATIONS, key => _mixedStorageKVStore.Upsert(key, _dummyClassInstance));
        }

        [IterationCleanup(Target = nameof(Upsert_ConcurrentInserts_WithoutCompression))]
        public void Upsert_ConcurrentInserts_WithoutCompression_IterationCleanup()
        {
            _mixedStorageKVStore.Dispose();
        }

        // Concurrent reads without compression
        [GlobalSetup(Target = nameof(Upsert_ConcurrentReads_WithoutCompression))]
        public void Upsert_ConcurrentReads_WithoutCompression_GlobalSetup()
        {
            _mixedStorageKVStoreOptions = new()
            {
                PageSizeBits = 12, // 4 KB
                MemorySizeBits = 13, // 2 pages
                MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
            };
            //_mixedStorageKVStore = new ObjLogMixedStorageKVStore<int, string>(_mixedStorageKVStoreOptions);
            //_mixedStorageKVStore = new MemoryMixedStorageKVStore<int, string>(_mixedStorageKVStoreOptions);
            _mixedStorageKVStore = new MixedStorageKVStore<int, DummyClass>(_mixedStorageKVStoreOptions);
            Parallel.For(0, READ_NUM_OPERATIONS, key => _mixedStorageKVStore.Upsert(key, _dummyClassInstance));
        }

        [IterationSetup(Target = nameof(Upsert_ConcurrentReads_WithoutCompression))]
        public void Upsert_ConcurrentReads_WithoutCompression_IterationSetup()
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

        private async Task<(Status, DummyClass?)> ReadAsync(int key)
        {
            await Task.Yield();

            return await _mixedStorageKVStore.ReadAsync(key).ConfigureAwait(false);
        }

        [GlobalCleanup(Target = nameof(Upsert_ConcurrentReads_WithoutCompression))]
        public void Upsert_ConcurrentReads_WithoutCompression_GlobalCleanup()
        {
            _mixedStorageKVStore.Dispose();
        }

        [MessagePackObject]
        public class DummyClass
        {
            [Key(0)]
            public string? DummyString { get; set; }

            [Key(1)]
            public string[]? DummyStringArray { get; set; }

            [Key(2)]
            public int DummyInt { get; set; }

            [Key(3)]
            public int[]? DummyIntArray { get; set; }
        }
    }
}
