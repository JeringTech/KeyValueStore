using FASTER.core;
using MessagePack;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jering.KeyValueStore.Performance
{
    /// <summary>
    /// An implementation of <see cref="IMixedStorageKVStore{TKey, TValue}"/> that uses an object log.
    /// </summary>
    internal class ObjLogMixedStorageKVStore<TKey, TValue> : IMixedStorageKVStore<TKey, TValue>
    {
        private static readonly MixedStorageKVStoreOptions _defaultMixedStorageKeyValueStoreOptions = new();

        // Faster store
        private readonly FasterKV<TKey, TValue> _fasterKVStore;
        private readonly SimpleFunctions<TKey, TValue> _simpleFunctions = new();
        private readonly FasterKV<TKey, TValue>.ClientSessionBuilder<TValue, TValue, Empty> _clientSessionBuilder;

        // Sessions
        private readonly ThreadLocal<ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>> _threadLocalSession;
        private readonly ConcurrentQueue<ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>> _sessionPool = new();

        // Log
        private readonly LogAccessor<TKey, TValue> _logAccessor;

        // Serialization
        private readonly MessagePackSerializerOptions _messagePackSerializerOptions;

        // Disposal
        private bool _disposed;
        private readonly IDevice? _logDevice;
        private readonly IDevice? _objectLogDevice;

        /// <summary>
        /// Creates a <see cref="ObjLogMixedStorageKVStore{TKey, TValue}"/>.
        /// </summary>
        public ObjLogMixedStorageKVStore(MixedStorageKVStoreOptions? mixedStorageKeyValueStoreOptions = null)
        {
            mixedStorageKeyValueStoreOptions ??= _defaultMixedStorageKeyValueStoreOptions;
            LogSettings logSettings = CreateSettings(mixedStorageKeyValueStoreOptions);
            SerializerSettings<TKey, TValue> serializerSettings = new()
            {
                valueSerializer = () => new ObjLogValueSerializer<TValue>()
            };

            _fasterKVStore = new(mixedStorageKeyValueStoreOptions.IndexNumBuckets, logSettings, serializerSettings: serializerSettings);
            _clientSessionBuilder = _fasterKVStore.For(_simpleFunctions);
            _logDevice = logSettings.LogDevice; // _fasterKVStore.dispose doesn't dispose underlying log devices, so hold a references for manual disposal
            _objectLogDevice = logSettings.ObjectLogDevice;

            _logAccessor = _fasterKVStore.Log;
            _messagePackSerializerOptions = mixedStorageKeyValueStoreOptions.MessagePackSerializerOptions;
            _threadLocalSession = new(CreateSession, true);
        }

        /// <summary>
        /// Creates a <see cref="ObjLogMixedStorageKVStore{TKey, TValue}"/>.
        /// </summary>
        public ObjLogMixedStorageKVStore(FasterKV<TKey, TValue> fasterKVStore,
            MessagePackSerializerOptions? messagePackSerializerOptions = null)
        {
            _fasterKVStore = fasterKVStore;
            _clientSessionBuilder = _fasterKVStore.For(_simpleFunctions);
            // TODO can we get references to log devices?

            _logAccessor = _fasterKVStore.Log;
            _messagePackSerializerOptions = messagePackSerializerOptions ?? _defaultMixedStorageKeyValueStoreOptions.MessagePackSerializerOptions;
            _threadLocalSession = new(CreateSession, true);
        }

        /// <inheritdoc />
        public void Upsert(TKey key, TValue obj)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ObjLogMixedStorageKVStore<TKey, TValue>));
            }

            _threadLocalSession.Value.Upsert(key, obj);
        }

        /// <inheritdoc />
        public Status Delete(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ObjLogMixedStorageKVStore<TKey, TValue>));
            }

            return _threadLocalSession.Value.Delete(key);
        }

        /// <inheritdoc />
        public async ValueTask<(Status, TValue?)> ReadAsync(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ObjLogMixedStorageKVStore<TKey, TValue>));
            }

            ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>> session = GetPooledSession();

            (Status, TValue) result = (await session.ReadAsync(key).ConfigureAwait(false)).Complete();

            _sessionPool.Enqueue(session);

            return result; 
        }

        private ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>> GetPooledSession()
        {
            if (_sessionPool.TryDequeue(out ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>? result))
            {
                return result;
            }

            return CreateSession();
        }

        private ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>> CreateSession()
        {
            return _clientSessionBuilder.NewSession<SimpleFunctions<TKey, TValue>>();
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
                ObjectLogDevice = Devices.CreateLogDevice(Path.Combine(logDirectory, $"{logFileName}.obj.log"),
                    // TODO add option if we use this alternative
                    deleteOnClose: true),
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
                    foreach (ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>> session in _sessionPool)
                    {
                        session.Dispose();
                    }

                    foreach (ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>> session in _threadLocalSession.Values)
                    {
                        session.Dispose();
                    }

                    _threadLocalSession?.Dispose();
                    _fasterKVStore?.Dispose(); // Only safe to call after disposing all sessions
                    _logDevice?.Dispose();
                    _objectLogDevice?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
