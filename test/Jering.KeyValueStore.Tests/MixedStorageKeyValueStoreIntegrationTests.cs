using FASTER.core;
using MessagePack;
using System;
using System.Collections.Concurrent;
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
    /// <item>Handle class, struct and built-in value-type values</item>
    /// <item>Delete log files on dispose or finalize</item>
    /// <item>Truncate log files when disk limits reached</item>
    /// <item>Periodically perform log compaction. Log compaction runs concurrently with other operations.</item>
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
        public void UpsertReadAsyncDelete_AreThreadSafe()
        {
            // Arrange
            var dummyOptions = new MixedStorageKeyValueStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileName = nameof(UpsertReadAsyncDelete_AreThreadSafe),
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
            using var testSubject = new MixedStorageKeyValueStore<int, DummyClass>(dummyOptions);

            // Act and assert

            // Insert
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyClassInstance));

            // Read
            ConcurrentQueue<(Status, DummyClass?)> results = new();
            Parallel.For(0, numRecords, async key => results.Enqueue(await testSubject.ReadAsync(key).ConfigureAwait(false)));
            foreach ((Status status, DummyClass? result) in results)
            {
                Assert.Equal(Status.OK, status);
                Assert.Equal(dummyClassInstance, result); // See DummyClass.Equals
            }

            // Update
            dummyClassInstance.DummyInt = 20;
            dummyClassInstance.DummyString = "anotherDummyString";
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyClassInstance));

            // Read again to verify updates
            results.Clear();
            Parallel.For(0, numRecords, async key => results.Enqueue(await testSubject.ReadAsync(key).ConfigureAwait(false)));
            foreach ((Status status, DummyClass? result) in results)
            {
                Assert.Equal(Status.OK, status);
                Assert.Equal(dummyClassInstance, result); // See DummyClass.Equals
            }

            // Delete
            Parallel.For(0, numRecords, key => testSubject.Delete(key));

            //Read again to verify deletes
            results.Clear();
            Parallel.For(0, numRecords, async key => results.Enqueue(await testSubject.ReadAsync(key).ConfigureAwait(false)));
            foreach ((Status status, DummyClass? result) in results)
            {
                Assert.Equal(Status.NOTFOUND, status);
                Assert.Null(result);
            }
        }

        // MixedStorageKeyValueStore creates an ObjectLogDevice
        [Fact]
        public void HandlesVariableLengthStructValues()
        {
            // Arrange
            var dummyOptions = new MixedStorageKeyValueStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileName = nameof(HandlesVariableLengthStructValues),
                PageSizeBits = 12,
                MemorySizeBits = 13 // Limit to 8KB so we're testing both in-memory and disk-based operations
            };
            int numRecords = 10000;
            var dummyStructInstance = new DummyVariableLengthStruct()
            {
                // Populate with dummy values
                DummyString = "dummyString",
                DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
                DummyInt = 10,
                DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
            };
            using var testSubject = new MixedStorageKeyValueStore<int, DummyVariableLengthStruct>(dummyOptions);

            // Act and assert
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyStructInstance));
            var exceptionQueue = new ConcurrentQueue<Exception>();
            Parallel.For(0, numRecords, async key =>
            {
                try
                {
                    (Status status, DummyVariableLengthStruct result) = await testSubject.ReadAsync(key).ConfigureAwait(false);
                    Assert.Equal(Status.OK, status);
                    Assert.Equal(dummyStructInstance, result);
                }
                catch (Exception exception)
                {
                    exceptionQueue.Enqueue(exception);
                }
            });
            if (!exceptionQueue.IsEmpty)
            {
                throw new AggregateException(exceptionQueue);
            }
        }

        [Fact]
        public void HandlesFixedLengthStructValues()
        {
            // Arrange
            var dummyOptions = new MixedStorageKeyValueStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileName = nameof(HandlesFixedLengthStructValues),
                PageSizeBits = 12,
                MemorySizeBits = 13 // Limit to 8KB so we're testing both in-memory and disk-based operations
            };
            int numRecords = 10000;
            var dummyStructInstance = new DummyFixedLengthStruct()
            {
                // Populate with dummy values
                DummyByte = byte.MaxValue,
                DummyShort = short.MaxValue,
                DummyInt = int.MaxValue,
                DummyLong = long.MaxValue
            };
            using var testSubject = new MixedStorageKeyValueStore<int, DummyFixedLengthStruct>(dummyOptions);

            // Act and assert
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyStructInstance));
            var exceptionQueue = new ConcurrentQueue<Exception>();
            Parallel.For(0, numRecords, async key =>
            {
                try
                {
                    (Status status, DummyFixedLengthStruct result) = await testSubject.ReadAsync(key).ConfigureAwait(false);
                    Assert.Equal(Status.OK, status);
                    Assert.Equal(dummyStructInstance, result);
                }
                catch (Exception exception)
                {
                    exceptionQueue.Enqueue(exception);
                }
            });
            if (!exceptionQueue.IsEmpty)
            {
                throw new AggregateException(exceptionQueue);
            }
        }

        [Fact]
        public void HandlesPrimitiveValues()
        {
            // Arrange
            var dummyOptions = new MixedStorageKeyValueStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileName = nameof(HandlesPrimitiveValues),
                PageSizeBits = 12,
                MemorySizeBits = 13 // Limit to 8KB so we're testing both in-memory and disk-based operations
            };
            int numRecords = 10000;
            using var testSubject = new MixedStorageKeyValueStore<int, int>(dummyOptions);

            // Act and assert
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, key));
            var exceptionQueue = new ConcurrentQueue<Exception>();
            Parallel.For(0, numRecords, async key =>
            {
                try
                {
                    (Status status, int result) = await testSubject.ReadAsync(key).ConfigureAwait(false);
                    Assert.Equal(Status.OK, status);
                    Assert.Equal(key, result);
                }
                catch (Exception exception)
                {
                    exceptionQueue.Enqueue(exception);
                }
            });
            if (!exceptionQueue.IsEmpty)
            {
                throw new AggregateException(exceptionQueue);
            }
        }

        // TODO Finalizing should delete log files too, is there a way to test this?
        [Fact]
        public void DeletesLogFilesOnDispose()
        {
            // Arrange
            var dummyOptions = new MixedStorageKeyValueStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileName = nameof(DeletesLogFilesOnDispose),
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
            using var testSubject = new MixedStorageKeyValueStore<int, DummyClass>(dummyOptions);
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, dummyClassInstance)); // Creates log
            Assert.Single(Directory.EnumerateFiles(_fixture.TempDirectory, $"{nameof(DeletesLogFilesOnDispose)}*")); // Log and object log

            // Act
            testSubject.Dispose();

            // Assert
            Assert.Empty(Directory.EnumerateFiles(_fixture.TempDirectory)); // Logs deleted
        }

        [Fact]
        public void TruncatesLogFilesOnDiskSpaceLimitsReached()
        {
            // Arrange
            var dummyOptions = new MixedStorageKeyValueStoreOptions()
            {
                LogDirectory = _fixture.TempDirectory,
                LogFileName = nameof(TruncatesLogFilesOnDiskSpaceLimitsReached),
                PageSizeBits = 9, // 512 bytes
                MemorySizeBits = 10, // 1024 bytes
                SegmentSizeBits = 12, // 4 KB
                LogDiskSpaceBytes = 8192, // Maximum of three 4 KB segments (Faster truncates to 3 rather than 2 segments if this value is segment size * 2)
            };
            int numRecords = 1000; // Occupies more than max in-memory and disk space
            using var testSubject = new MixedStorageKeyValueStore<int, string>(dummyOptions);

            // Act
            Parallel.For(0, numRecords, key => testSubject.Upsert(key, "dummyString")); // Creates log, truncates whenever log exceeds limit

            // Assert
            // Three 4 KB segment for each log
            Assert.Equal(3, Directory.EnumerateFiles(_fixture.TempDirectory, $"{nameof(TruncatesLogFilesOnDiskSpaceLimitsReached)}*").Count());
        }

        //[Fact]
        //public void PerformsLogCompactionPeriodically()
        //{
        //}

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
