using FASTER.core;
using MessagePack;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace Jering.KeyValueStore
{
    /// <summary>
    /// The default implementation of <see cref="IMixedStorageKeyValueStore{TKey, TValue}"/>.
    /// </summary>
    public class MixedStorageKeyValueStore<TKey, TValue> : IMixedStorageKeyValueStore<TKey, TValue>
    {
        private static readonly MixedStorageKeyValueStoreOptions _defaultOptions = new();

        // Faster store
        private FasterKV<TKey, SpanByte> _fasterKVStore;

        // Sessions
        private ConcurrentQueue<ClientSession<TKey, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<TKey>>> _sessionPool = new();

        // Log
        private LogAccessor<TKey, SpanByte> _logAccessor;
        private IDevice? _logDevice;

        //// Log compaction
        //private readonly int _timeBetweenCompactionsMS;
        //private readonly int _numReadOnlyRecordsBeforeCompation;
        //private CancellationTokenSource _compactionCancellationTokenSource;

        // Disposal
        private bool _disposed;

        /// <summary>
        /// Creates a <see cref="MixedStorageKeyValueStore{TKey, TValue}"/>.
        /// </summary>
        public MixedStorageKeyValueStore(MixedStorageKeyValueStoreOptions? options = null)
        {
            options ??= _defaultOptions;
            LogSettings LogSettings = CreateSettings(options);
            _fasterKVStore = new(options.IndexNumBuckets, LogSettings);
            _logAccessor = _fasterKVStore.Log;

            //_timeBetweenCompactionsMS = options.TimeBetweenCompactionsMS;
            //_numReadOnlyRecordsBeforeCompation = options.NumReadOnlyRecordsBeforeCompaction;
            //_compactionCancellationTokenSource = new CancellationTokenSource();
            //Task.Run(CompactionLoop);
        }

        /// <summary>
        /// Creates a <see cref="MixedStorageKeyValueStore{TKey, TValue}"/>.
        /// </summary>
        public MixedStorageKeyValueStore(FasterKV<TKey, SpanByte> fasterKVStore,
            int timeBetweenCompactionsMS = 10000) // Attempt log compaction every 10 seconds
        {
            _fasterKVStore = fasterKVStore;
            //_timeBetweenCompactionsMS = timeBetweenCompactionsMS;
            _logAccessor = _fasterKVStore.Log;
        }

        /// <inheritdoc />
        public void Upsert(TKey key, TValue obj)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKeyValueStore<TKey, TValue>));
            }

            byte[] objBytes = MessagePackSerializer.Serialize(obj);

            // TODO Consider ThreadLocal session for synchronous operations after Faster stabilizes
            ClientSession<TKey, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<TKey>> session = GetPooledSession();

            unsafe
            {
                fixed (byte* pointer = objBytes)
                {
                    var spanByte = SpanByte.FromFixedSpan(objBytes);
                    session.Upsert(key, spanByte);
                }
            }

            _sessionPool.Enqueue(session);
        }

        /// <inheritdoc />
        public Status Delete(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKeyValueStore<TKey, TValue>));
            }

            // TODO Consider ThreadLocal session for synchronous operations after Faster stabilizes
            ClientSession<TKey, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<TKey>> session = GetPooledSession();

            Status result = session.Delete(key);

            _sessionPool.Enqueue(session);

            return result;
        }

        /// <inheritdoc />
        public async ValueTask<(Status, TValue?)> ReadAsync(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKeyValueStore<TKey, TValue>));
            }

            ClientSession<TKey, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<TKey>> session = GetPooledSession();

            (Status status, SpanByteAndMemory spanByteAndMemory) = (await session.ReadAsync(key).ConfigureAwait(false)).Complete();

            using IMemoryOwner<byte> memoryOwner = spanByteAndMemory.Memory;

            (Status status, TValue?) result = (status, status != Status.OK ? default : MessagePackSerializer.Deserialize<TValue>(memoryOwner.Memory));

            _sessionPool.Enqueue(session);

            return result;
        }

        internal virtual ClientSession<TKey, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<TKey>> GetPooledSession()
        {
            if (_sessionPool.TryDequeue(out ClientSession<TKey, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<TKey>>? result))
            {
                return result;
            }

            // SpanByteFunctions<> isn't reusable. If we reuse instances, we get NullReferenceExceptions
            return _fasterKVStore.For(new SpanByteFunctions<TKey>()).NewSession<SpanByteFunctions<TKey>>();
        }

        internal virtual LogSettings CreateSettings(MixedStorageKeyValueStoreOptions options)
        {
            // Log settings
            string logDirectory = options.LogDirectory ?? Path.Combine(Path.GetTempPath(), "FasterLogs");
            string logFileName = options.LogFileName ?? Guid.NewGuid().ToString();
            var logSettings = new LogSettings
            {
                LogDevice = _logDevice = Devices.CreateLogDevice(Path.Combine(logDirectory, $"{logFileName}.log"),
                    deleteOnClose: options.DeleteLogOnClose,
                    capacity: options.LogDiskSpaceBytes),
                PageSizeBits = options.PageSizeBits,
                MemorySizeBits = options.MemorySizeBits,
                SegmentSizeBits = options.SegmentSizeBits
            };

            return logSettings;
        }

        //internal virtual async Task CompactionLoop()
        //{
        //    CancellationToken cancellationToken = _compactionCancellationTokenSource.Token;

        //    while (!_disposed && !cancellationToken.IsCancellationRequested)
        //    {
        //        try
        //        {
        //            await Task.Delay(_timeBetweenCompactionsMS, cancellationToken);

        //            // (oldest entries here) BeginAddress <= HeadAddress (where the in-memory region begins) <= SafeReadOnlyAddress (entries between here and tail updated in-place) < TailAddress (entries added here)
        //            if ((_logAccessor.SafeReadOnlyAddress - _logAccessor.BeginAddress) / _logAccessor.FixedRecordSize < _numReadOnlyRecordsBeforeCompation)
        //            {
        //                continue;
        //            }

        //            long compactUntilAddress = (long)(_logAccessor.BeginAddress + 0.2 * (_logAccessor.SafeReadOnlyAddress - _logAccessor.BeginAddress));

        //            ClientSession<TKey, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<TKey>> session = GetPooledSession();

        //            session.Compact(compactUntilAddress, true);

        //            _sessionPool.Enqueue(session);
        //        }
        //        catch (OperationCanceledException)
        //        {
        //            return;
        //        }
        //        catch
        //        {
        //            // Compaction failed or we disposed the instance. If we disposed the instance we could get a NullReferenceException since _sessionPool
        //            // may be null, we could also get an ObjectDisposedException since sessions may be disposed.

        //            // TODO we should log here
        //        }
        //    }
        //}

        /// <summary>
        /// Disposes this instance. This method is not thread-safe. It should only be called after all other calls to this instance's methods have returned.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the instance. This method is not thread-safe. It should only be called after all other calls to this instance's methods have returned.
        /// </summary>
        /// <param name="disposing">True if the object is disposing or false if it is finalizing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    //_compactionCancellationTokenSource?.Cancel();

                    foreach (ClientSession<TKey, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<TKey>> session in _sessionPool)
                    {
                        session.Dispose();
                    }

                    // We might dispose of the instance while compacting.This means the session used to compact
                    // may not get disposed, which in turn means disposing of the Faster store might throw
                    // (requires sessions be disposed before it's disposed).
                    // Just catch for now, it's all managed resources anyway.
                    //
                    // TODO Consider a SemaphoreSlim if this approach fails.
                    try
                    {
                        _fasterKVStore?.Dispose(); // Only safe to call after disposing all sessions
                    }
                    catch
                    {
                        // Do nothing
                    }
                    _logDevice?.Dispose();

#pragma warning disable CS8625
                    // TODO If we don't set _logDevice to null here and we don't add a using block to DeletesLogFilesOnDispose,
                    // integration tests fail when run together. Most likely an issue with its _logDevice's finalizer.
                    _logDevice = null;
                    _fasterKVStore = null;
                    _sessionPool = null;
                    //_compactionCancellationTokenSource = null;
#pragma warning restore CS8625
                }

                _disposed = true;
            }
        }
    }
}
