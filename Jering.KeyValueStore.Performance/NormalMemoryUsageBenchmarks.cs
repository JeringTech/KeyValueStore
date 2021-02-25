using BenchmarkDotNet.Attributes;
using FASTER.core;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Jering.KeyValueStore.Performance
{
    [MemoryDiagnoser]
    public class NormalMemoryUsageBenchmarks
    {
        private MixedStorageKeyValueStore<int, string> _mixedStorageKeyValueStore;
        private MixedStorageKeyValueStoreOptions _mixedStorageKeyValueStoreOptions = new MixedStorageKeyValueStoreOptions()
        {
            PageSizeBits = 25, // 33.5 MB pages
            MemorySizeBits = 26 // 67 MB
        };

        [GlobalSetup(Target = nameof(Upsert_ConcurrentInserts))]
        public void Upsert_ConcurrentInserts_GlobalSetup()
        {
            // Verify that our benchmark works
            Console.WriteLine($"Verifying {nameof(Upsert_ConcurrentInserts_IterationSetup)} ...");
            _mixedStorageKeyValueStore = new MixedStorageKeyValueStore<int, string>(_mixedStorageKeyValueStoreOptions);
            Parallel.For(0, 10000, UpsertAction);
            Parallel.For(0, 10000, async key =>
            {
                (Status status, string result) = await _mixedStorageKeyValueStore.ReadAsync(key).ConfigureAwait(false);
                Assert.Equal(Status.OK, status);
                Assert.Equal("dummyString", result);
            });
            _mixedStorageKeyValueStore.Dispose();
            Console.WriteLine($"{nameof(Upsert_ConcurrentInserts_IterationSetup)} producing expected output\n");
        }

        [IterationSetup(Target = nameof(Upsert_ConcurrentInserts))]
        public void Upsert_ConcurrentInserts_IterationSetup()
        {
            _mixedStorageKeyValueStore = new MixedStorageKeyValueStore<int, string>(_mixedStorageKeyValueStoreOptions);
        }

        [Benchmark]
        public void Upsert_ConcurrentInserts()
        {
            Parallel.For(0, 10_000_000, UpsertAction);
        }

        private void UpsertAction(int key)
        {
            _mixedStorageKeyValueStore.Upsert(key, "dummyString");
        }

        [IterationCleanup(Target = nameof(Upsert_ConcurrentInserts))]
        public void Upsert_ConcurrentInserts_IterationCleanup()
        {
            _mixedStorageKeyValueStore.Dispose();
        }
    }
}
