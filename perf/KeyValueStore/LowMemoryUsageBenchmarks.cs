using BenchmarkDotNet.Attributes;
using FASTER.core;
using MessagePack;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Jering.KeyValueStore.Performance
{
    // TODO
    // - Benchmark different types of values
    //   - Primitive
    //   - Fixed length struct
    //   - Variable length struct
    //   - Class
    // - Benchmark different storage ratios
    //   - Half in memory, half on disk
    //   - All in memory
    // - Benchmark delete operation
    [MemoryDiagnoser]
    public class LowMemoryUsageBenchmarks
    {
#pragma warning disable CS8618
        private MixedStorageKVStore<int, DummyClass> _mixedStorageKVStore;
        private MixedStorageKVStoreOptions _mixedStorageKVStoreOptions;
#pragma warning restore CS8618
        private const int NUM_INSERT_OPERATIONS = 350_000;
        private const int NUM_READ_OPERATIONS = 75_000;
        private readonly ConcurrentQueue<ValueTask<(Status, DummyClass?)>> _readTasks = new();
        private readonly ConcurrentQueue<Task> _upsertTasks = new();
        private readonly DummyClass _dummyClassInstance = new()
        {
            // Populate with dummy values
            DummyString = "dummyString",
            DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
            DummyInt = 10,
            DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
        };

        // Concurrent inserts without compression
        [GlobalSetup(Target = nameof(Inserts_WithoutCompression))]
        public void Inserts_WithoutCompression_GlobalSetup()
        {
            _mixedStorageKVStoreOptions = new()
            {
                PageSizeBits = 12, // 4 KB
                MemorySizeBits = 13, // 2 pages
                TimeBetweenLogCompactionsMS = -1, // Disable log compactions
                MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
            };
        }

        [IterationSetup(Target = nameof(Inserts_WithoutCompression))]
        public void Inserts_WithoutCompression_IterationSetup()
        {
            _mixedStorageKVStore = new MixedStorageKVStore<int, DummyClass>(_mixedStorageKVStoreOptions);
            _upsertTasks.Clear();
        }

        [Benchmark]
        public async Task Inserts_WithoutCompression()
        {
            Parallel.For(0, NUM_INSERT_OPERATIONS, key => _upsertTasks.Enqueue(_mixedStorageKVStore.UpsertAsync(key, _dummyClassInstance)));
            await Task.WhenAll(_upsertTasks).ConfigureAwait(false);
        }

        [IterationCleanup(Target = nameof(Inserts_WithoutCompression))]
        public void Inserts_WithoutCompression_IterationCleanup()
        {
            _mixedStorageKVStore.Dispose();
        }

        // Concurrent reads without compression
        [GlobalSetup(Target = nameof(Reads_WithoutCompression))]
        public async Task Reads_WithoutCompression_GlobalSetup()
        {
            _mixedStorageKVStoreOptions = new()
            {
                PageSizeBits = 12, // 4 KB
                MemorySizeBits = 13, // 2 pages
                TimeBetweenLogCompactionsMS = -1, // Disable log compactions
                MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
            };
            _mixedStorageKVStore = new MixedStorageKVStore<int, DummyClass>(_mixedStorageKVStoreOptions);
            Parallel.For(0, NUM_READ_OPERATIONS, key => _upsertTasks.Enqueue(_mixedStorageKVStore.UpsertAsync(key, _dummyClassInstance)));
            await Task.WhenAll(_upsertTasks).ConfigureAwait(false);
        }

        [IterationSetup(Target = nameof(Reads_WithoutCompression))]
        public void Reads_WithoutCompression_IterationSetup()
        {
            _readTasks.Clear();
        }

        [Benchmark]
        public async Task Reads_WithoutCompression()
        {
            Parallel.For(0, NUM_READ_OPERATIONS, key => _readTasks.Enqueue(_mixedStorageKVStore.ReadAsync(key)));
            foreach (ValueTask<(Status, DummyClass?)> task in _readTasks)
            {
                if (task.IsCompleted)
                {
                    continue;
                }
                await task.ConfigureAwait(false);
            }
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
