using FASTER.core;
using MessagePack;
using System;
using System.IO;

namespace Jering.KeyValueStore
{
    /// <summary>
    /// Options for a <see cref="MixedStorageKVStore{TKey, TValue}"/>.
    /// </summary>
    public class MixedStorageKVStoreOptions
    {
        /// <summary>
        /// <para>The number of buckets in Faster's main index.</para>
        /// <para>Each bucket is 64 bits.</para>
        /// <para>This value is ignored if a <see cref="FasterKV{Key, Value}"/> instance is supplied to the <see cref="MixedStorageKVStore{TKey, TValue}"/> constructor.</para>
        /// <para>Defaults to 1048576 (64 MB index).</para>
        /// </summary>
        public long IndexNumBuckets { get; set; } = 1L << 20;

        /// <summary>
        /// <para>The size of a page in Faster's log.</para>
        /// <para>A page is a contiguous block of in-memory or on-disk storage.</para>
        /// <para>This value is ignored if a <see cref="FasterKV{Key, Value}"/> instance is supplied to the <see cref="MixedStorageKVStore{TKey, TValue}"/> constructor.</para>
        /// <para>Defaults to 25 (2^25 = 33.5 MB).</para>
        /// </summary>
        public int PageSizeBits { get; set; } = 25;

        /// <summary>
        /// <para>The size of the in-memory region of Faster's log.</para>
        /// <para>If the log outgrows this region, overflow is moved to its on-disk region.</para>
        /// <para>This value is ignored if a <see cref="FasterKV{Key, Value}"/> instance is supplied to the <see cref="MixedStorageKVStore{TKey, TValue}"/> constructor.</para>
        /// <para>Defaults to 26 (2^26 = 67 MB).</para>
        /// </summary>
        public int MemorySizeBits { get; set; } = 26; // 67 MB

        /// <summary>
        /// <para>The size of a segment of the on-disk region of Faster's log.</para>
        /// <para>What is a segment? The on-disk region of the log is stored across multiple files, each file is referred to as a segment.</para>
        /// <para>For performance reasons, segments are "pre-allocated". This means they are not created empty and left to grow gradually, instead they are created at the size specified by this value and populated gradually.</para>
        /// <para>This value is ignored if a <see cref="FasterKV{Key, Value}"/> instance is supplied to the <see cref="MixedStorageKVStore{TKey, TValue}"/> constructor.</para>
        /// <para>Defaults to 28 (268 MB).</para>
        /// </summary>
        public int SegmentSizeBits { get; set; } = 28;

        /// <summary>
        /// <para>The directory containing the on-disk region of Faster's log.</para>
        /// <para>If this value is <c>null</c> or an empty string, log files are placed in "&lt;temporary path&gt;/FasterLogs" where 
        /// "&lt;temporary path&gt;" is the value returned by <see cref="Path.GetTempPath"/>.</para>
        /// <para>Note that nothing is written to disk while your log fits in-memory.</para>
        /// <para>This value is ignored if a <see cref="FasterKV{Key, Value}"/> instance is supplied to the <see cref="MixedStorageKVStore{TKey, TValue}"/> constructor.</para>
        /// <para>Defaults to <c>null</c>.</para>
        /// </summary>
        public string? LogDirectory { get; set; } = null;

        /// <summary>
        /// <para>The Faster log filename prefix.</para>
        /// <para>The on-disk region of the log is stored across multiple files. Each file is referred to as a segment.
        /// Each segment has file name "&lt;log file name prefix&gt;.log.&lt;segment number&gt;".
        /// </para>
        /// <para>If this value is <c>null</c> or an empty string, a random <see cref="Guid"/> is used as the prefix.</para>
        /// <para>This value is ignored if a <see cref="FasterKV{Key, Value}"/> instance is supplied to the <see cref="MixedStorageKVStore{TKey, TValue}"/> constructor.</para>
        /// <para>Defaults to <c>null</c>.</para>
        /// </summary>
        public string? LogFileNamePrefix { get; set; } = null;

        /// <summary>
        /// <para>The time between Faster log compaction attempts.</para>
        /// <para>If this value is negative, log compaction is disabled.</para>
        /// <para>Defaults to 60000</para>
        /// </summary>
        public int TimeBetweenLogCompactionsMS { get; set; } = 60_000;

        // TODO what if a FasterKV instance is supplied and MemorySizeBits is 0 or negative?
        /// <summary>
        /// <para>The initial log compaction threshold.</para>
        /// <para>Initially, log compactions only run when the Faster log's safe-readonly region's size is larger than or equal to this value.</para>
        /// <para>If log compactions run 5 times in a row, this value is doubled. Why? Consider the situation where the safe-readonly region is already 
        /// compact, but still larger than the threshold. Not increasing the threshold would result in numerous redundant compaction runs.</para>
        /// <para>If this value is less than or equal to 0, the initial log compaction threshold is 2 * memory size in bytes (<see cref="MemorySizeBits"/>).</para>
        /// <para>Defaults to 0</para>
        /// </summary>
        public long InitialLogCompactionThresholdBytes { get; internal set; } = 0;

        /// <summary>
        /// <para>The value specifying whether log files are deleted when the <see cref="MixedStorageKVStore{TKey, TValue}"/> is disposed or finalized (at which points underlying log files are closed).</para>
        /// <para>This value is ignored if a <see cref="FasterKV{Key, Value}"/> instance is supplied to the <see cref="MixedStorageKVStore{TKey, TValue}"/> constructor.</para>
        /// <para>Defaults to <c>true</c>.</para>
        /// </summary>
        public bool DeleteLogOnClose { get; set; } = true;

        /// <summary>
        /// <para>The options for serializing data using MessagePack C#.</para>
        /// <para>MessagePack C# is an efficient binary serialization library. Refer to their <a href="https://github.com/neuecc/MessagePack-CSharp">documentation</a>
        /// for details.</para>
        /// <para>Defaults to <see cref="MessagePackSerializerOptions.Standard"/> with compression using <see cref="MessagePackCompression.Lz4BlockArray"/>.</para>
        /// </summary>
        public MessagePackSerializerOptions MessagePackSerializerOptions { get; set; } = MessagePackSerializerOptions.
            Standard.
            WithCompression(MessagePackCompression.Lz4BlockArray);
    }
}
