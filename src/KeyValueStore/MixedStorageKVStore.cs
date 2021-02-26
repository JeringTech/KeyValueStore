using FASTER.core;
using MessagePack;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jering.KeyValueStore
{
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

        // Serialization
        private readonly MessagePackSerializerOptions _messagePackSerializerOptions;
        private readonly ThreadLocal<ArrayBufferWriter<byte>> _threadLocalArrayBufferWriter;
        private readonly ConcurrentQueue<ArrayBufferWriter<byte>> _arrayBufferWriterPool = new();

        // Disposal
        private bool _disposed;
        private readonly IDevice? _logDevice;

        /// <summary>
        /// Creates a <see cref="MixedStorageKVStore{TKey, TValue}"/>.
        /// </summary>
        public MixedStorageKVStore(MixedStorageKVStoreOptions? mixedStorageKeyValueStoreOptions = null)
        {
            mixedStorageKeyValueStoreOptions ??= _defaultMixedStorageKVStoreOptions;
            LogSettings logSettings = CreateSettings(mixedStorageKeyValueStoreOptions);

            _fasterKVStore = new(mixedStorageKeyValueStoreOptions.IndexNumBuckets, logSettings);
            _clientSessionBuilder = _fasterKVStore.For(_spanByteFunctions);
            _logDevice = logSettings.LogDevice; // _fasterKVStore.dispose doesn't dispose the underlying log device, so hold a reference for manual disposal

            _logAccessor = _fasterKVStore.Log;
            _messagePackSerializerOptions = mixedStorageKeyValueStoreOptions.MessagePackSerializerOptions;
            _threadLocalArrayBufferWriter = new(CreateArrayBufferWriter, true);
            _threadLocalSession = new(CreateSession, true);

        }

        /// <summary>
        /// Creates a <see cref="MixedStorageKVStore{TKey, TValue}"/>.
        /// </summary>
        public MixedStorageKVStore(FasterKV<SpanByte, SpanByte> fasterKVStore,
            MessagePackSerializerOptions? messagePackSerializerOptions = null)
        {
            _fasterKVStore = fasterKVStore;
            _clientSessionBuilder = _fasterKVStore.For(_spanByteFunctions);
            // TODO can we get a reference to the log device?

            _logAccessor = _fasterKVStore.Log;
            _messagePackSerializerOptions = messagePackSerializerOptions ?? _defaultMixedStorageKVStoreOptions.MessagePackSerializerOptions;
            _threadLocalArrayBufferWriter = new(CreateArrayBufferWriter, true);
            _threadLocalSession = new(CreateSession, true);
        }

        // TODO fast paths for fixed size keys and values
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
                    var keySpanByte = SpanByte.FromFixedSpan(span.Slice(0, keyLength));
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

        // Required for ThreadLocal<ArrayBufferWriter<byte>>
        private static ArrayBufferWriter<byte> CreateArrayBufferWriter()
        {
            return new();
        }

        private LogSettings CreateSettings(MixedStorageKVStoreOptions options)
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
                    foreach (ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> session in _sessionPool)
                    {
                        session.Dispose();
                    }

                    foreach (ClientSession<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty, SpanByteFunctions<Empty>> session in _threadLocalSession.Values)
                    {
                        session.Dispose();
                    }

                    _threadLocalSession?.Dispose();
                    _fasterKVStore?.Dispose(); // Only safe to call after disposing all sessions
                    _logDevice?.Dispose();
                    _threadLocalArrayBufferWriter?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
