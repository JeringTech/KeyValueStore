using FASTER.core;
using System;
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

        // Serialization
        private static readonly SimpleFunctions<TKey, TValue> _simpleFunctions = new();

        // Logs
        private readonly LogAccessor<TKey, TValue> _logAccessor;
        private readonly int _timeBetweenCompactionsMS;
        private IDevice _logDevice;
        private IDevice _objectLogDevice;

        // Thread local session
        [ThreadStatic]
        private static ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>? _session;

        // Shared pool
        private static readonly ConcurrentQueue<ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>> _sessionPool = new();

        // Disposal
        private static readonly ConcurrentBag<ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>> _allSessions = new();
        private readonly FasterKV<TKey, TValue> _fasterKVStore;
        private bool _disposed;

        /// <summary>
        /// Creates a <see cref="MixedStorageKeyValueStore{TKey, TValue}"/>.
        /// </summary>
        public MixedStorageKeyValueStore(MixedStorageKeyValueStoreOptions? options = null)
        {
            options ??= _defaultOptions;
            (SerializerSettings<TKey, TValue> serializerSettings, LogSettings LogSettings) = CreateSettings(options);
            _fasterKVStore = new(options.IndexNumBuckets, LogSettings, serializerSettings: serializerSettings);
            _logAccessor = _fasterKVStore.Log;
            _timeBetweenCompactionsMS = options.TimeBetweenCompactionsMS;
        }

        /// <summary>
        /// Creates a <see cref="MixedStorageKeyValueStore{TKey, TValue}"/>.
        /// </summary>
        public MixedStorageKeyValueStore(FasterKV<TKey, TValue> fasterKVStore,
            int timeBetweenCompactionsMS = 10000) // Attempt log compaction every 10 seconds
        {
            _fasterKVStore = fasterKVStore;
            _timeBetweenCompactionsMS = timeBetweenCompactionsMS;
            _logAccessor = _fasterKVStore.Log;
        }

        /// <inheritdoc />
        public void Upsert(TKey key, TValue obj)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKeyValueStore<TKey, TValue>));
            }

            GetSession().Upsert(key, obj);
        }

        /// <inheritdoc />
        public Status Delete(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKeyValueStore<TKey, TValue>));
            }

            return GetSession().Delete(key);
        }

        /// <inheritdoc />
        public async ValueTask<(Status, TValue)> ReadAsync(TKey key)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MixedStorageKeyValueStore<TKey, TValue>));
            }

            ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>? session = GetSession();
            ValueTask<FasterKV<TKey, TValue>.ReadAsyncResult<TValue, TValue, Empty>> readAsyncResult = session.ReadAsync(key);

            // Retain thread-local in sync path.
            // This speed ups in-memory reads at the cost of slightly slower disk reads.
            if (readAsyncResult.IsCompleted)
            {
                return readAsyncResult.Result.Complete();
            }

            // Going async - remove session from thread-local.
            // If we don't do this, the session is captured by the continuation, which may run on a different thread.
            // If that happens, we might erroneously use a session from multiple threads at the same time. 
            // - https://github.com/microsoft/FASTER/issues/403#issuecomment-781408293
            _session = null;
            (Status, TValue) result = (await readAsyncResult.ConfigureAwait(false)).Complete();

            // Return session to shared pool on async thread
            _sessionPool.Enqueue(session);
            return result;
        }

        internal virtual ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>> GetSession()
        {
            if (_session != null)
            {
                return _session;
            }

            if (_sessionPool.TryDequeue(out ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>? result))
            {
                return _session = result;
            }

            ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>>? session = _fasterKVStore.For(_simpleFunctions).NewSession<SimpleFunctions<TKey, TValue>>();
            _allSessions.Add(session);
            return _session = session;
        }

        internal virtual (SerializerSettings<TKey, TValue>, LogSettings) CreateSettings(MixedStorageKeyValueStoreOptions options)
        {
            // Serializer settings
            var serializerSettings = new SerializerSettings<TKey, TValue>
            {
                valueSerializer = () => new DefaultBinaryObjectSerializer<TValue>()
            };

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

            // ObjectLogDevice only necessary for non-fixed size structs and reference types.
            // TODO We're creating the object log device for all structs. Figure out how to detect if struct is fixed size.
            // TODO Alternatives for variable length structs - https://microsoft.github.io/FASTER/docs/fasterkv-basics/#handling-variable-length-keys-and-values
            if (!typeof(TValue).IsValueType || !typeof(TValue).IsPrimitive)
            {
                logSettings.ObjectLogDevice = _objectLogDevice = Devices.CreateLogDevice(Path.Combine(logDirectory, $"{logFileName}.obj.log"),
                    deleteOnClose: options.DeleteObjectLogOnClose,
                    capacity: options.ObjectLogDiskSpaceBytes);
            }

            return (serializerSettings, logSettings);
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
                    foreach (ClientSession<TKey, TValue, TValue, TValue, Empty, SimpleFunctions<TKey, TValue>> session in _allSessions)
                    {
                        session.Dispose();
                    }

                    _fasterKVStore?.Dispose(); // Only safe to call after disposing all sessions
                    _logDevice?.Dispose();
                    _objectLogDevice?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
