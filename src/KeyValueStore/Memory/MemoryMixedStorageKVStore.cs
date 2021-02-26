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
    /// An implementation of <see cref="IMixedStorageKVStore{TKey, TValue}"/> that uses <see cref="Memory{T}"/>.
    /// </summary>
    internal class MemoryMixedStorageKVStore<TKey, TValue> : IMixedStorageKVStore<TKey, TValue>
    {
        private static readonly MixedStorageKVStoreOptions _defaultMixedStorageKeyValueStoreOptions = new();

        // Faster store
        private readonly FasterKV<TKey, Memory<byte>> _fasterKVStore;
        private readonly MemoryFunctions<TKey, byte, Empty> _memoryFunctions = new();
        private readonly FasterKV<TKey, Memory<byte>>.ClientSessionBuilder<Memory<byte>, (IMemoryOwner<byte>, int), Empty> _clientSessionBuilder;

        // Sessions
        private readonly ThreadLocal<ClientSession<TKey, Memory<byte>, Memory<byte>, (IMemoryOwner<byte>, int), Empty, MemoryFunctions<TKey, byte, Empty>>> _threadLocalSession;
        private readonly ConcurrentQueue<ClientSession<TKey, Memory<byte>, Memory<byte>, (IMemoryOwner<byte>, int), Empty, MemoryFunctions<TKey, byte, Empty>>> _sessionPool = new();

        // Log
        private readonly LogAccessor<TKey, Memory<byte>> _logAccessor;

        // Serialization
        private readonly MessagePackSerializerOptions _messagePackSerializerOptions;
        private readonly ThreadLocal<ArrayBufferWriter<byte>> _threadLocalArrayBufferWriter;

        // Disposal
        private bool _disposed;
        private readonly IDevice? _logDevice;

        /// <summary>
        /// Creates a <see cref="MemoryMixedStorageKVStore{TKey, TValue}"/>.
        /// </summary>
        public MemoryMixedStorageKVStore(MixedStorageKVStoreOptions? mixedStorageKeyValueStoreOptions = null)
        {
            mixedStorageKeyValueStoreOptions ??= _defaultMixedStorageKeyValueStoreOptions;
            LogSettings logSettings = CreateSettings(mixedStorageKeyValueStoreOptions);

            _fasterKVStore = new(mixedStorageKeyValueStoreOptions.IndexNumBuckets, logSettings);
            _clientSessionBuilder = _fasterKVStore.For(_memoryFunctions);
            _logDevice = logSettings.LogDevice; // _fasterKVStore.dispose doesn't dispose the underlying log device, so hold a reference for manual disposal

            _logAccessor = _fasterKVStore.Log;
            _messagePackSerializerOptions = mixedStorageKeyValueStoreOptions.MessagePackSerializerOptions;
            _threadLocalArrayBufferWriter = new(CreateArrayBufferWriter, true);
            _threadLocalSession = new(CreateSession, true);

        }

        /// <summary>
        /// Creates a <see cref="MemoryMixedStorageKVStore{TKey, TValue}"/>.
        /// </summary>
        public MemoryMixedStorageKVStore(FasterKV<TKey, Memory<byte>> fasterKVStore,
            MessagePackSerializerOptions? messagePackSerializerOptions = null)
        {
            _fasterKVStore = fasterKVStore;
            _clientSessionBuilder = _fasterKVStore.For(_memoryFunctions);
            // TODO can we get a reference to the log device?

            _logAccessor = _fasterKVStore.Log;
            _messagePackSerializerOptions = messagePackSerializerOptions ?? _defaultMixedStorageKeyValueStoreOptions.MessagePackSerializerOptions;
            _threadLocalArrayBufferWriter = new(CreateArrayBufferWriter, true);
            _threadLocalSession = new(CreateSession, true);
        }

        /// <inheritdoc />
        public void Upsert(TKey key, TValue obj)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MemoryMixedStorageKVStore<TKey, Memory<byte>>));
            }

            // Serialize
            //ArrayBufferWriter<byte> arrayBufferWriter = _threadLocalArrayBufferWriter.Value;
            byte[] bytes = MessagePackSerializer.Serialize(obj, _messagePackSerializerOptions);
            //ReadOnlyMemory<byte> memory = arrayBufferWriter.WrittenMemory;

            _threadLocalSession.Value.Upsert(key, new Memory<byte>(bytes));

            //arrayBufferWriter.Clear();
        }

        /// <inheritdoc />
        public Status Delete(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MemoryMixedStorageKVStore<TKey, Memory<byte>>));
            }

            return _threadLocalSession.Value.Delete(key);
        }

        /// <inheritdoc />
        public async ValueTask<(Status, TValue?)> ReadAsync(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MemoryMixedStorageKVStore<TKey, Memory<byte>>));
            }

            ClientSession<TKey, Memory<byte>, Memory<byte>, (IMemoryOwner<byte>, int), Empty, MemoryFunctions<TKey, byte, Empty>> session = GetPooledSession();

            (Status status, (IMemoryOwner<byte>, int) result) = (await session.ReadAsync(key).ConfigureAwait(false)).Complete();

            _sessionPool.Enqueue(session);

            using IMemoryOwner<byte> memoryOwner = result.Item1;

            return (status, status != Status.OK ? default : MessagePackSerializer.Deserialize<TValue>(memoryOwner.Memory, _messagePackSerializerOptions)); 
        }

        private ClientSession<TKey, Memory<byte>, Memory<byte>, (IMemoryOwner<byte>, int), Empty, MemoryFunctions<TKey, byte, Empty>> GetPooledSession()
        {
            if (_sessionPool.TryDequeue(out ClientSession<TKey, Memory<byte>, Memory<byte>, (IMemoryOwner<byte>, int), Empty, MemoryFunctions<TKey, byte, Empty>>? result))
            {
                return result;
            }

            return CreateSession();
        }

        private ClientSession<TKey, Memory<byte>, Memory<byte>, (IMemoryOwner<byte>, int), Empty, MemoryFunctions<TKey, byte, Empty>> CreateSession()
        {
            return _clientSessionBuilder.NewSession<MemoryFunctions<TKey, byte, Empty>>();
        }

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
                    foreach (ClientSession<TKey, Memory<byte>, Memory<byte>, (IMemoryOwner<byte>, int), Empty, MemoryFunctions<TKey, byte, Empty>> session in _sessionPool)
                    {
                        session.Dispose();
                    }

                    foreach (ClientSession<TKey, Memory<byte>, Memory<byte>, (IMemoryOwner<byte>, int), Empty, MemoryFunctions<TKey, byte, Empty>> session in _threadLocalSession.Values)
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
