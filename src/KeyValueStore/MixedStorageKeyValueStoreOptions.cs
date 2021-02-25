using System;

namespace Jering.KeyValueStore
{
    public class MixedStorageKeyValueStoreOptions
    {
        // Index
        public long IndexNumBuckets { get; set; } = 1L << 20; // 64 MB index

        // Log
        public string? LogDirectory { get; set; } = null;
        public string? LogFileName { get; set; } = null;
        public int PageSizeBits { get; set; } = 25; // 33.5 MB pages
        public int MemorySizeBits { get; set; } = 26; // 67 MB
        public int SegmentSizeBits { get; set; } = 28; // 250 MB
        public int TimeBetweenCompactionsMS { get; set; } = 10000;

        public long LogDiskSpaceBytes { get; set; } = 1L << 28; // 250 MB
        public bool DeleteLogOnClose { get; set; } = true;

        public long ObjectLogDiskSpaceBytes { get; set; } = 1L << 28; // 250 MB
        public bool DeleteObjectLogOnClose { get; set; } = true;
    }
}
