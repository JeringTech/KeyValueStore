using FASTER.core;
using MessagePack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Jering.KeyValueStore.Tests
{
    /// <summary>
    /// Verifies behaviour of <see cref="MixedStorageKeyValueStore"/> and its underlying <see cref="FasterKV{TKey, TValue}"/> instance. They:
    /// <list type="bullet">
    /// <item>Handle concurrent insert, update, read and delete operations</item>
    /// <item>Handle reference-type and value-type keys and values</item>
    /// <item>Delete log files on dispose</item>
    /// <item>Truncate log files when disk limits reached</item>
    /// </list>
    /// </summary>
    public class MixedStorageKeyValueStoreIntegrationTests : IClassFixture<MixedStorageKeyValueStoreIntegrationTestsFixture>
    {
        private readonly MixedStorageKeyValueStoreIntegrationTestsFixture _fixture;

        public MixedStorageKeyValueStoreIntegrationTests(MixedStorageKeyValueStoreIntegrationTestsFixture fixture)
        {
            _fixture = fixture;
        }

        // TODO Interleave operations
        [Fact]
        public async Task UpsertReadAsyncDelete_AreThreadSafe()
        {
            // Arrange
            var dummyOptions = new MixedStorageKVStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileNamePrefix = nameof(UpsertReadAsyncDelete_AreThreadSafe),
                PageSizeBits = 12,
                MemorySizeBits = 13 // Limit to 8KB so we're testing both in-memory and disk-based operations
            };
            var dummyClassInstance = new DummyClass()
            {
                // Populate with dummy values
                DummyString = "dummyString",
                DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
                DummyInt = 10,
                DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
            };
            int numRecords = 10000;
            //using var testSubject = new ObjLogMixedStorageKVStore<int, DummyClass>(dummyOptions);
            //using var testSubject = new MemoryMixedStorageKVStore<int, DummyClass>(dummyOptions);
            using var testSubject = new MixedStorageKVStore<int, DummyClass>(dummyOptions);

            // Act and assert

            // Insert
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyClassInstance));

            // Read
            await ReadAndVerifyValuesAsync(numRecords, testSubject, Status.OK, dummyClassInstance).ConfigureAwait(false);

            // Update
            dummyClassInstance.DummyInt = 20;
            dummyClassInstance.DummyString = "anotherDummyString";
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyClassInstance));

            // Verify updates
            await ReadAndVerifyValuesAsync(numRecords, testSubject, Status.OK, dummyClassInstance).ConfigureAwait(false);

            // Delete
            Parallel.For(0, numRecords, key => testSubject.Delete(key));

            // Verify deletes
            await ReadAndVerifyValuesAsync(numRecords, testSubject, Status.NOTFOUND, null).ConfigureAwait(false);
        }

        [Fact]
        public async Task HandlesObjectKeysAndValues()
        {
            // Arrange
            var dummyOptions = new MixedStorageKVStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileNamePrefix = nameof(HandlesObjectKeysAndValues),
                PageSizeBits = 12,
                MemorySizeBits = 13 // Limit to 8KB so we're testing both in-memory and disk-based operations
            };
            int numRecords = 10000;
            using var testSubject = new MixedStorageKVStore<string, string>(dummyOptions);

            // Act and assert

            // Insert
            Parallel.For(0, numRecords, key =>
            {
                string keyAsString = key.ToString();
                testSubject.Upsert(keyAsString, keyAsString);
            });

            // Read
            List<Task<(Status, string?)>> readTasks = new();
            for (int key = 0; key < numRecords; key++)
            {
                readTasks.Add(ReadAsync(key.ToString(), testSubject));
            }
            await Task.WhenAll(readTasks).ConfigureAwait(false);

            // Verify
            Parallel.For(0, numRecords, key =>
            {
                (Status status, string? result) = readTasks[key].Result;
                Assert.Equal(Status.OK, status);
                Assert.Equal(key.ToString(), result); // See DummyClass.Equals
            });
        }

        [Fact]
        public async Task HandlesVariableLengthStructKeysAndValues()
        {
            // Arrange
            var dummyOptions = new MixedStorageKVStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileNamePrefix = nameof(HandlesVariableLengthStructKeysAndValues),
                PageSizeBits = 12,
                MemorySizeBits = 13 // Limit to 8KB so we're testing both in-memory and disk-based operations
            };
            var dummyStructInstance = new DummyVariableLengthStruct()
            {
                // Populate with dummy values
                DummyString = "dummyString",
                DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
                DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
            };
            int numRecords = 10000;
            using var testSubject = new MixedStorageKVStore<DummyVariableLengthStruct, DummyVariableLengthStruct>(dummyOptions);

            // Act and assert

            // Insert
            Parallel.For(0, numRecords, key =>
            {
                DummyVariableLengthStruct localDummyStructInstance = dummyStructInstance;
                localDummyStructInstance.DummyInt = key;
                testSubject.Upsert(localDummyStructInstance, localDummyStructInstance);
            });

            // Read
            List<Task<(Status, DummyVariableLengthStruct)>> readTasks = new();
            for (int key = 0; key < numRecords; key++)
            {
                DummyVariableLengthStruct localDummyStructInstance = dummyStructInstance;
                localDummyStructInstance.DummyInt = key;
                readTasks.Add(ReadAsync(localDummyStructInstance, testSubject));
            }
            await Task.WhenAll(readTasks).ConfigureAwait(false);

            // Verify
            for (int key = 0; key < numRecords; key++)
            {
                (Status status, DummyVariableLengthStruct result) = readTasks[key].Result;
                Assert.Equal(Status.OK, status);
                DummyVariableLengthStruct localDummyStructInstance = dummyStructInstance;
                localDummyStructInstance.DummyInt = key;
                Assert.Equal(localDummyStructInstance, result);
            };
        }

        [Fact]
        public async Task HandlesFixedLengthStructKeysAndValues()
        {
            // Arrange
            var dummyOptions = new MixedStorageKVStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileNamePrefix = nameof(HandlesVariableLengthStructKeysAndValues),
                PageSizeBits = 12,
                MemorySizeBits = 13 // Limit to 8KB so we're testing both in-memory and disk-based operations
            };
            var dummyStructInstance = new DummyFixedLengthStruct()
            {
                // Populate with dummy values
                DummyByte = byte.MaxValue,
                DummyShort = short.MaxValue,
                DummyLong = long.MaxValue
            };
            int numRecords = 10000;
            using var testSubject = new MixedStorageKVStore<DummyFixedLengthStruct, DummyFixedLengthStruct>(dummyOptions);

            // Act and assert

            // Insert
            Parallel.For(0, numRecords, key =>
            {
                DummyFixedLengthStruct localDummyStructInstance = dummyStructInstance;
                localDummyStructInstance.DummyInt = key;
                testSubject.Upsert(localDummyStructInstance, localDummyStructInstance);
            });

            // Read
            List<Task<(Status, DummyFixedLengthStruct)>> readTasks = new();
            for (int key = 0; key < numRecords; key++)
            {
                DummyFixedLengthStruct localDummyStructInstance = dummyStructInstance;
                localDummyStructInstance.DummyInt = key;
                readTasks.Add(ReadAsync(localDummyStructInstance, testSubject));
            }
            await Task.WhenAll(readTasks).ConfigureAwait(false);

            // Verify
            for (int key = 0; key < numRecords; key++)
            {
                (Status status, DummyFixedLengthStruct result) = readTasks[key].Result;
                Assert.Equal(Status.OK, status);
                DummyFixedLengthStruct localDummyStructInstance = dummyStructInstance;
                localDummyStructInstance.DummyInt = key;
                Assert.Equal(localDummyStructInstance, result);
            };
        }

        [Fact]
        public async Task HandlesPrimitiveKeysAndValues()
        {
            // Arrange
            var dummyOptions = new MixedStorageKVStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileNamePrefix = nameof(HandlesPrimitiveKeysAndValues),
                PageSizeBits = 12,
                MemorySizeBits = 13 // Limit to 8KB so we're testing both in-memory and disk-based operations
            };
            const int dummyValue = 12345;
            const int numRecords = 10000;
            using var testSubject = new MixedStorageKVStore<int, int>(dummyOptions);

            // Act and assert
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyValue));
            await ReadAndVerifyValuesAsync(numRecords, testSubject, Status.OK, dummyValue).ConfigureAwait(false);
        }

        // TODO Finalizing should delete log files too, is there a way to test this?
        [Fact]
        public void DeletesLogFilesOnDispose()
        {
            // Arrange
            var dummyOptions = new MixedStorageKVStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileNamePrefix = nameof(DeletesLogFilesOnDispose),
                PageSizeBits = 9, // Minimum
                MemorySizeBits = 10 // Minimum
            };
            var dummyClassInstance = new DummyClass()
            {
                // Populate with dummy values
                DummyString = "dummyString",
                DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
                DummyInt = 10,
                DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
            };
            int numRecords = 50; // Just enough to make sure log files are created. Segment size isn't exceeded (only 1 of each log file).
            var testSubject = new MixedStorageKVStore<int, DummyClass>(dummyOptions);
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyClassInstance)); // Creates log
            Assert.Single(Directory.EnumerateFiles(_fixture.TempDirectory, $"{nameof(DeletesLogFilesOnDispose)}*")); // Log and object log

            // Act
            testSubject.Dispose();

            // Assert
            Assert.Empty(Directory.EnumerateFiles(_fixture.TempDirectory)); // Logs deleted
        }

        #region Helpers
        private static async Task ReadAndVerifyValuesAsync<TValue>(int numRecords,
            IMixedStorageKVStore<int, TValue> mixedStorageKeyValueStore,
            Status expectedStatus,
            TValue? expectedResult)
        {
            // Read
            List<Task<(Status, TValue?)>> readTasks = new();
            for (int key = 0; key < numRecords; key++)
            {
                readTasks.Add(ReadAsync(key, mixedStorageKeyValueStore));
            }
            await Task.WhenAll(readTasks).ConfigureAwait(false);

            // Verify
            Parallel.For(0, numRecords, index =>
            {
                (Status status, TValue? result) = readTasks[index].Result;
                Assert.Equal(expectedStatus, status);
                Assert.Equal(expectedResult, result); // See DummyClass.Equals
            });
        }

        private static async Task<(Status, TValue?)> ReadAsync<TKey, TValue>(TKey key, IMixedStorageKVStore<TKey, TValue> mixedStorageKeyValueStore)
        {
            // Parallel.For doesn't await async actions, so we use Task.Yield to ensure operations run completely asynchronously and complete as
            // quickly as possible.
            await Task.Yield();

            return await mixedStorageKeyValueStore.ReadAsync(key).ConfigureAwait(false);
        }
        #endregion

        #region Types
        [MessagePackObject]
        public struct DummyFixedLengthStruct
        {
            [Key(0)]
            public byte DummyByte { get; set; }

            [Key(1)]
            public short DummyShort { get; set; }

            [Key(2)]
            public int DummyInt { get; set; }

            [Key(3)]
            public long DummyLong { get; set; }

            public override bool Equals(object? obj)
            {
                if (obj is not DummyFixedLengthStruct castObject)
                {
                    return false;
                }

                return castObject.DummyByte == DummyByte &&
                    castObject.DummyShort == DummyShort &&
                    castObject.DummyInt == DummyInt &&
                    castObject.DummyLong == DummyLong;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(DummyByte, DummyShort, DummyInt, DummyLong);
            }
        }

        [MessagePackObject]
        public struct DummyVariableLengthStruct
        {
            [Key(0)]
            public string? DummyString { get; set; }

            [Key(1)]
            public string[]? DummyStringArray { get; set; }

            [Key(2)]
            public int DummyInt { get; set; }

            [Key(3)]
            public int[]? DummyIntArray { get; set; }

            public override bool Equals(object? obj)
            {
                if (obj is not DummyVariableLengthStruct castObject)
                {
                    return false;
                }

                return castObject.DummyString == DummyString &&
                    castObject.DummyInt == DummyInt &&
#pragma warning disable CS8604 // Arrays should never be null in these tests, throw if they are
                    castObject.DummyStringArray.SequenceEqual(DummyStringArray) &&
                    castObject.DummyIntArray.SequenceEqual(DummyIntArray);
#pragma warning restore CS8604
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(DummyString, DummyStringArray, DummyInt, DummyIntArray);
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

            public override bool Equals(object? obj)
            {
                if (obj is not DummyClass castObject)
                {
                    return false;
                }

                return castObject.DummyString == DummyString &&
                    castObject.DummyInt == DummyInt &&
#pragma warning disable CS8604 // Arrays should never be null in these tests, throw if they are
                    castObject.DummyStringArray.SequenceEqual(DummyStringArray) &&
                    castObject.DummyIntArray.SequenceEqual(DummyIntArray);
#pragma warning restore CS8604
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(DummyString, DummyStringArray, DummyInt, DummyIntArray);
            }
        }
        #endregion
    }

    public class MixedStorageKeyValueStoreIntegrationTestsFixture : IDisposable
    {
        public string TempDirectory { get; } = Path.Combine(Path.GetTempPath(), nameof(MixedStorageKeyValueStoreIntegrationTests));

        public MixedStorageKeyValueStoreIntegrationTestsFixture()
        {
            TryDeleteDirectory();
            Directory.CreateDirectory(TempDirectory);
        }

        private void TryDeleteDirectory()
        {
            try
            {
                Directory.Delete(TempDirectory, true);
            }
            catch
            {
                // Do nothing
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool _)
        {
            TryDeleteDirectory();
        }
    }
}
