using FASTER.core;
using System.Buffers;

namespace Jering.KeyValueStore
{
    public class SpanByteFunctions<TKey> : SpanByteFunctions<TKey, SpanByteAndMemory, Empty>
    {
        private readonly MemoryPool<byte> _memoryPool;

        public SpanByteFunctions(MemoryPool<byte>? memoryPool = default, bool locking = false) : base(locking)
        {
            _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
        }

        /// <inheritdoc />
        public unsafe override void SingleReader(ref TKey key, ref SpanByte input, ref SpanByte value, ref SpanByteAndMemory dst)
        {
            value.CopyTo(ref dst, _memoryPool);
        }

        /// <inheritdoc />
        public unsafe override void ConcurrentReader(ref TKey key, ref SpanByte input, ref SpanByte value, ref SpanByteAndMemory dst)
        {
            if (locking) value.SpinLock();
            value.CopyTo(ref dst, _memoryPool);
            if (locking) value.Unlock();
        }
    }
}
