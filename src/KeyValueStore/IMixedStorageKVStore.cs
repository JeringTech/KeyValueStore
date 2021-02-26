using FASTER.core;
using System;
using System.Threading.Tasks;

namespace Jering.KeyValueStore
{
    /// <summary>
    /// An abstraction for a key-value store that spans memory and disk.
    /// </summary>
    /// <typeparam name="TKey">The type of the key-value store's key.</typeparam>
    /// <typeparam name="TValue">The type of the key-value store's values.</typeparam>
    public interface IMixedStorageKVStore<TKey, TValue> : IDisposable
    {
        /// <summary>
        /// Updates or inserts a record.
        /// </summary>
        /// <param name="key">The key of the record.</param>
        /// <param name="obj">The new value of the record.</param>
        /// <exception cref="ObjectDisposedException">Thrown if the instance is disposed.</exception>
        void Upsert(TKey key, TValue obj);

        /// <summary>
        /// Deletes a record.
        /// </summary>
        /// <param name="key">The key of the record to delete.</param>
        /// <exception cref="ObjectDisposedException">Thrown if the instance is disposed.</exception>
        Status Delete(TKey key);

        /// <summary>
        /// Reads a record asynchronously.
        /// </summary>
        /// <param name="key">The key of the record to read.</param>
        /// <exception cref="ObjectDisposedException">Thrown if the instance is disposed.</exception>
        ValueTask<(Status, TValue?)> ReadAsync(TKey key);
    }
}
