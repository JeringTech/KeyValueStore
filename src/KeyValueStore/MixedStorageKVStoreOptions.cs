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
        /// <para>Defaults to 1048576 (64 MB index).</para>
        /// </summary>
        public long IndexNumBuckets { get; set; } = 1L << 20;

        /// <summary>
        /// <para>The size of a page in the log.</para>
        /// <para>A page is a contiguous block of storage in-memory or on-disk.</para>
        /// <para>Defaults to 25 (2^25 = 33.5 MB).</para>
        /// </summary>
        public int PageSizeBits { get; set; } = 25;

        /// <summary>
        /// <para>The size of the in-memory region of the log.</para>
        /// <para>If the log outgrows this region, overflow is moved to its on-disk region.</para>
        /// <para>Defaults to 26 (2^26 = 67 MB).</para>
        /// </summary>
        public int MemorySizeBits { get; set; } = 26; // 67 MB

        /// <summary>
        /// <para>The size of a segment of the on-disk region of the log.</para>
        /// <para>The on-disk region of the log is stored across multiple files. Each file is referred to as a segment.</para>
        /// <para>Defaults to 28 (268 MB).</para>
        /// </summary>
        public int SegmentSizeBits { get; set; } = 28;

        /// <summary>
        /// <para>The directory containing the on-disk region of the log.</para>
        /// <para>If this value is <c>null</c> or an empty string, log files are placed in "&lt;temporary path&gt;/FasterLogs" where 
        /// "&lt;temporary path&gt;" is the value returned by <see cref="Path.GetTempPath"/>.</para>
        /// <para>Note that nothing is written to disk while your log fits in-memory.</para>
        /// <para>Defaults to <c>null</c>.</para>
        /// </summary>
        public string? LogDirectory { get; set; } = null;

        /// <summary>
        /// <para>The log-file-name prefix.</para>
        /// <para>The on-disk region of the log is stored across multiple files. Each file is referred to as a segment.
        /// Each segment has file name "&lt;log file name prefix&gt;.log.&lt;segment number&gt;".
        /// </para>
        /// <para>If this value is <c>null</c> or an empty string, a random <see cref="Guid"/> is used as the prefix.</para>
        /// <para>Defaults to <c>null</c>.</para>
        /// </summary>
        public string? LogFileNamePrefix { get; set; } = null;

        /// <summary>
        /// <para>The value specifying whether log files are deleted when the <see cref="MixedStorageKVStore{TKey, TValue}"/> is disposed or finalized</para>
        /// <para>Defaults to <c>true</c>.</para>
        /// </summary>
        public bool DeleteLogOnClose { get; set; } = true;

        /// <summary>
        /// <para>The options for serializing values.</para>
        /// <para>MessagePack C# is an efficient binary serialization library. Refer to their <a href="https://github.com/neuecc/MessagePack-CSharp">documentation</a>
        /// for details.</para>
        /// <para>Defaults to <see cref="MessagePackSerializerOptions.Standard"/> with compression using <see cref="MessagePackCompression.Lz4BlockArray"/>.</para>
        /// </summary>
        public MessagePackSerializerOptions MessagePackSerializerOptions { get; set; } = MessagePackSerializerOptions.
            Standard.
            WithCompression(MessagePackCompression.Lz4BlockArray);
    }
}
