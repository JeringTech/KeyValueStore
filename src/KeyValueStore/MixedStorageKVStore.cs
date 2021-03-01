using FASTER.core;
using MessagePack;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jering.KeyValueStore
{
    // TODO
    // - Fast paths for fixed size keys and values. We need an equivalent of FASTER.core.Utility.IsBlittableType to check 
    //   if a key/value type is blittable. If it is, we can either use a fast path or create a FasterKV instance with blittable key/value type.
    /// <summary>
    /// The default implementation of <see cref="IMixedStorageKVStore{TKey, TValue}"/>.
    /// </summary>
    public class MixedStorageKVStore<TKey, TValue> : IMixedStorageKVStore<TKey, TValue>
    {
        private static readonly MixedStorageKVStoreOptions _defaultMixedStorageKVStoreOptions = new();

        // Faster store
        private readonly FasterKV<SpanByte, SpanByte> _fasterKVStore;
        private readonly SpanByteFunctions<Empty> _spanByteFunctions = new();
        private readonly FasterKV<SpanByte, SpanByte>.ClientSessionBuilder<SpanByte, SpanByteAndMemory, Empty> _clientSessionBuilder;

        // Sessions
        private readonly ThreadLocal<ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>>> _threadLocalSession;
        private readonly ConcurrentQueue<ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>>> _sessionPool = new();

        // Log
        private readonly LogAccessor<SpanByte, SpanByte> _logAccessor;
        private readonly CancellationTokenSource? _logCompactionCancellationTokenSource;
        private readonly int _timeBetweenLogCompactionsMS;
        private long _logCompactionThresholdBytes = 0;
        private byte _numConsecutiveLogCompactions = 0;
        private const byte NUM_CONSECUTIVE_COMPACTIONS_BEFORE_THRESHOLD_INCREASE = 5;

        // Serialization
        private readonly MessagePackSerializerOptions _messagePackSerializerOptions;
        private readonly ThreadLocal<ArrayBufferWriter<byte>> _threadLocalArrayBufferWriter;
        private readonly ConcurrentQueue<ArrayBufferWriter<byte>> _arrayBufferWriterPool = new();

        // Logging
        private readonly ILogger<MixedStorageKVStore<TKey, TValue>>? _logger;
        private readonly bool _logTrace;
        private readonly bool _logWarning;

        // Disposal
        private bool _disposed;
        private readonly IDevice? _logDevice;

        /// <summary>
        /// Creates a <see cref="MixedStorageKVStore{TKey, TValue}"/>.
        /// </summary>
        public MixedStorageKVStore(MixedStorageKVStoreOptions? mixedStorageKVStoreOptions = null,
            ILogger<MixedStorageKVStore<TKey, TValue>>? logger = null,
            FasterKV<SpanByte, SpanByte>? fasterKVStore = null)
        {
            mixedStorageKVStoreOptions ??= _defaultMixedStorageKVStoreOptions;

            // Store
            if (fasterKVStore == null)
            {
                LogSettings logSettings = CreateLogSettings(mixedStorageKVStoreOptions);
                _logDevice = logSettings.LogDevice; // _fasterKVStore.dispose doesn't dispose the underlying log device, so hold a reference for immediate manual disposal
                _fasterKVStore = new(mixedStorageKVStoreOptions.IndexNumBuckets, logSettings);
            }
            else
            {
                _fasterKVStore = fasterKVStore;
            }

            // Session
            _clientSessionBuilder = _fasterKVStore.For(_spanByteFunctions);
            _threadLocalSession = new(CreateSession, true);

            // Log
            _logAccessor = _fasterKVStore.Log;
            _timeBetweenLogCompactionsMS = mixedStorageKVStoreOptions.TimeBetweenLogCompactionsMS;
            if (_timeBetweenLogCompactionsMS > -1)
            {
                _logCompactionThresholdBytes = mixedStorageKVStoreOptions.InitialLogCompactionThresholdBytes;
                _logCompactionThresholdBytes = _logCompactionThresholdBytes <= 0 ? (long)Math.Pow(2, mixedStorageKVStoreOptions.MemorySizeBits) * 2 : _logCompactionThresholdBytes;
                _logCompactionCancellationTokenSource = new CancellationTokenSource();
                Task.Run(LogCompactionLoop);
            }

            // Serialization
            _messagePackSerializerOptions = mixedStorageKVStoreOptions.MessagePackSerializerOptions;
            _threadLocalArrayBufferWriter = new(() => new(), true);

            // Logging
            _logger = logger;
            _logTrace = _logger?.IsEnabled(LogLevel.Trace) ?? false;
            _logWarning = _logger?.IsEnabled(LogLevel.Warning) ?? false;
        }

        /// <inheritdoc />
        public void Upsert(TKey key, TValue obj)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKVStore<TKey, TValue>));
            }

            // Session
            ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> session = _threadLocalSession.Value;

            // Serialize
            ArrayBufferWriter<byte> arrayBufferWriter = _threadLocalArrayBufferWriter.Value;
            MessagePackSerializer.Serialize(arrayBufferWriter, key, _messagePackSerializerOptions);
            int keyLength = arrayBufferWriter.WrittenCount;
            MessagePackSerializer.Serialize(arrayBufferWriter, obj, _messagePackSerializerOptions);
            ReadOnlySpan<byte> span = arrayBufferWriter.WrittenSpan;

            // Upsert
            unsafe
            {
                fixed (byte* pointer = span)
                {
                    var keySpanByte = SpanByte.FromFixedSpan(span[0..keyLength]);
                    var objSpanByte = SpanByte.FromFixedSpan(span[keyLength..]);
                    session.Upsert(ref keySpanByte, ref objSpanByte);
                }
            }

            // Clear memory pool
            arrayBufferWriter.Clear();
        }

        /// <inheritdoc />
        public Status Delete(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKVStore<TKey, TValue>));
            }

            // Session
            ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> session = _threadLocalSession.Value;

            // Serialize
            ArrayBufferWriter<byte> arrayBufferWriter = _threadLocalArrayBufferWriter.Value;
            MessagePackSerializer.Serialize(arrayBufferWriter, key, _messagePackSerializerOptions);
            ReadOnlySpan<byte> span = arrayBufferWriter.WrittenSpan;

            // Delete
            Status result;
            unsafe
            {
                fixed (byte* pointer = span)
                {
                    var keySpanByte = SpanByte.FromFixedSpan(span);
                    result = session.Delete(keySpanByte);
                }
            }

            // Clear memory pool
            arrayBufferWriter.Clear();

            return result;
        }

        /// <inheritdoc />
        public async ValueTask<(Status, TValue?)> ReadAsync(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKVStore<TKey, TValue>));
            }

            // Session
            ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> session = GetPooledSession();

            // Serialize
            ArrayBufferWriter<byte> arrayBufferWriter = GetPooledArrayBufferWriter();  // If we use a ThreadLocal ArrayBufferWriter, we might call Clear on the wrong instance if the continuation is on a different thread
            MessagePackSerializer.Serialize(arrayBufferWriter, key, _messagePackSerializerOptions);
            ReadOnlyMemory<byte> memory = arrayBufferWriter.WrittenMemory;

            // Read
            Status status;
            SpanByteAndMemory spanByteAndMemory;
            using (MemoryHandle memoryHandle = memory.Pin())
            {
                SpanByte keySpanByte;
                unsafe
                {
                    keySpanByte = SpanByte.FromPointer((byte*)memoryHandle.Pointer, memory.Length);
                }

                (status, spanByteAndMemory) = (await session.ReadAsync(keySpanByte).ConfigureAwait(false)).Complete();
            }

            // Clean up
            arrayBufferWriter.Clear();
            _arrayBufferWriterPool.Enqueue(arrayBufferWriter);
            _sessionPool.Enqueue(session);

            // Deserialize
            using IMemoryOwner<byte> memoryOwner = spanByteAndMemory.Memory;

            return (status, status != Status.OK ? default : MessagePackSerializer.Deserialize<TValue>(memoryOwner.Memory, _messagePackSerializerOptions));
        }

        private async Task LogCompactionLoop()
        {
#pragma warning disable CS8602 // If compaction loop is running, cts is not null (see constructor)
            CancellationToken cancellationToken = _logCompactionCancellationTokenSource.Token;
#pragma warning restore CS8602

            while (!_disposed && !_logCompactionCancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_timeBetweenLogCompactionsMS, cancellationToken);

                    // (oldest entries here) BeginAddress <= HeadAddress (where the in-memory region begins) <= SafeReadOnlyAddress (entries between here and tail updated in-place) < TailAddress (entries added here)
                    long safeReadOnlyRegionByteSize = _logAccessor.SafeReadOnlyAddress - _logAccessor.BeginAddress;
                    if (safeReadOnlyRegionByteSize < _logCompactionThresholdBytes)
                    {
                        if (_logTrace)
                        {
                            _logger.LogTrace(string.Format(Strings.LogTrace_SkippingLogCompaction, safeReadOnlyRegionByteSize, _logCompactionThresholdBytes));
                        }
                        _numConsecutiveLogCompactions = 0;
                        continue;
                    }

                    // Compact
                    long compactUntilAddress = (long)(_logAccessor.BeginAddress + 0.2 * (_logAccessor.SafeReadOnlyAddress - _logAccessor.BeginAddress));
                    _threadLocalSession.Value.Compact(compactUntilAddress, true);
                    _numConsecutiveLogCompactions++;

                    if (_logTrace)
                    {
                        _logger.LogTrace(string.Format(Strings.LogTrace_LogCompacted, 
                            safeReadOnlyRegionByteSize, 
                            _logAccessor.SafeReadOnlyAddress - _logAccessor.BeginAddress,
                            _numConsecutiveLogCompactions));
                    }

                    // Update threshold
                    // Note that we can't simply check whether safeReadOnlyRegionByteSize has changed - when the log is compact, safeReadOnlyRegionByteSize may change (increase or decrease)
                    // by small amounts every compaction. This is because records are shifted from head to tail, i.e. the set of records in the safe readonly region changes.
                    if (_numConsecutiveLogCompactions >= NUM_CONSECUTIVE_COMPACTIONS_BEFORE_THRESHOLD_INCREASE)
                    {
                        _logCompactionThresholdBytes *= 2; // Max long is ~9200 petabytes, overflow is not an issue for now
                        if (_logTrace)
                        {
                            _logger.LogTrace(string.Format(Strings.LogTrace_LogCompactionThresholdIncreased, _logCompactionThresholdBytes / 2, _logCompactionThresholdBytes));
                        }
                        _numConsecutiveLogCompactions = 0;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // Compaction failed or we disposed of the instance. If we disposed of the instance, the next while loop boolean expression evaluation returns false.
                    // If compaction failed, we try again after a delay.
                    if (_logWarning)
                    {
                        _logger.LogWarning(string.Format(Strings.LogWarning_Exception, exception.Message));
                    }
                }
            }
        }

        private ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> GetPooledSession()
        {
            if (_sessionPool.TryDequeue(out ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>>? result))
            {
                return result;
            }

            return CreateSession();
        }

        private ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> CreateSession()
        {
            return _clientSessionBuilder.NewSession<SpanByteFunctions<Empty>>();
        }

        private ArrayBufferWriter<byte> GetPooledArrayBufferWriter()
        {
            if (_arrayBufferWriterPool.TryDequeue(out ArrayBufferWriter<byte>? result))
            {
                return result;
            }

            return new();
        }

        private LogSettings CreateLogSettings(MixedStorageKVStoreOptions options)
        {
            // Log settings
            string logDirectory = string.IsNullOrWhiteSpace(options.LogDirectory) ? Path.Combine(Path.GetTempPath(), "FasterLogs") : options.LogDirectory;
            string logFileName = string.IsNullOrWhiteSpace(options.LogFileNamePrefix) ? Guid.NewGuid().ToString() : options.LogFileNamePrefix;

            var logSettings = new LogSettings
            {
                LogDevice = Devices.CreateLogDevice(Path.Combine(logDirectory, $"{logFileName}.log"),
                    deleteOnClose: options.DeleteLogOnClose),
                PageSizeBits = options.PageSizeBits,
                MemorySizeBits = options.MemorySizeBits,
                SegmentSizeBits = options.SegmentSizeBits
            };

            return logSettings;
        }

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
                    _logCompactionCancellationTokenSource?.Cancel();
                    _logCompactionCancellationTokenSource?.Dispose(); // Should not be necessary to call Dispose if we call Cancel, but no harm

                    foreach (ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> session in _sessionPool)
                    {
                        session.Dispose();
                    }

                    foreach (ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> session in _threadLocalSession.Values)
                    {
                        session.Dispose(); // Dispose synchronously completes all pending operations, so we should not get exceptions if we're in the middle of log compaction
                    }

                    _threadLocalSession.Dispose();
                    _fasterKVStore.Dispose(); // Only safe to call after disposing all sessions
                    _logDevice?.Dispose();
                    _threadLocalArrayBufferWriter.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
