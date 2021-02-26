using FASTER.core;
using System.Buffers;

namespace Jering.KeyValueStore
{
    /// <summary>
    /// Callback functions with:
    /// <list type="bullet">
    /// <item>Generic key</item>
    /// <item><see cref="SpanByte"/> value</item>
    /// <item><see cref="SpanByte"/> input</item>
    /// <item><see cref="SpanByteAndMemory"/> output</item>
    /// <item><see cref="Empty"/> context</item>
    /// </list>
    /// </summary>
    /// <typeparam name="TKey">The type of the key-value store's key.</typeparam>
    public class SpanByteFunctions<TKey> : SpanByteFunctions<TKey, SpanByteAndMemory, Empty>
    {
        /// <inheritdoc />
        public unsafe override void SingleReader(ref TKey key, ref SpanByte input, ref SpanByte value, ref SpanByteAndMemory dst)
        {
            value.CopyTo(ref dst, MemoryPool<byte>.Shared);
        }

        /// <inheritdoc />
        public unsafe override void ConcurrentReader(ref TKey key, ref SpanByte input, ref SpanByte value, ref SpanByteAndMemory dst)
        {
            value.CopyTo(ref dst, MemoryPool<byte>.Shared);
        }
    }
}
