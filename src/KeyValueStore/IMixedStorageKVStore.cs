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
    public interface IMixedStorageKVStore<TKey, TValue>
    {
        /// <summary>
        /// Gets the underlying <see cref="FasterKV{TKey, TValue}"/> instance.
        /// </summary>
        FasterKV<SpanByte, SpanByte> FasterKV { get; }

        /// <summary>
        /// Updates or inserts a record asynchronously.
        /// </summary>
        /// <param name="key">The record's key.</param>
        /// <param name="obj">The record's new value.</param>
        /// <returns>The task representing the asynchronous operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the instance or a dependency is disposed.</exception>
        /// <remarks>This method is thread-safe.</remarks>
        Task UpsertAsync(TKey key, TValue obj);

        /// <summary>
        /// Deletes a record asynchronously.
        /// </summary>
        /// <param name="key">The record's key.</param>
        /// <returns>The task representing the asynchronous operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the instance or a dependency is disposed.</exception>
        /// <remarks>This method is thread-safe.</remarks>
        ValueTask<Status> DeleteAsync(TKey key);

        /// <summary>
        /// Reads a record asynchronously.
        /// </summary>
        /// <param name="key">The record's key.</param>
        /// <returns>The task representing the asynchronous operation.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the instance or a dependency is disposed.</exception>
        /// <remarks>This method is thread-safe.</remarks>
        ValueTask<(Status, TValue?)> ReadAsync(TKey key);
    }
}
