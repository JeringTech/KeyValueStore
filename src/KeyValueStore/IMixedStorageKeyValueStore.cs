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
    public interface IMixedStorageKeyValueStore<TKey, TValue> : IDisposable
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="obj"></param>
        /// <exception cref="ObjectDisposedException">Thrown if the instance is disposed.</exception>
        void Upsert(TKey key, TValue obj);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <exception cref="ObjectDisposedException">Thrown if the instance is disposed.</exception>
        Status Delete(TKey key);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <exception cref="ObjectDisposedException">Thrown if the instance is disposed.</exception>
        ValueTask<(Status, TValue?)> ReadAsync(TKey key);
    }
}
