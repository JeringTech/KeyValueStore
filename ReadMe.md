# Jering.KeyValueStore
[![Build Status](https://dev.azure.com/JeringTech/KeyValueStore/_apis/build/status/Jering.KeyValueStore-CI?branchName=main)](https://dev.azure.com/JeringTech/KeyValueStore/_build/latest?definitionId=1?branchName=main)
[![codecov](https://codecov.io/gh/JeringTech/KeyValueStore/branch/main/graph/badge.svg)](https://codecov.io/gh/JeringTech/KeyValueStore)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/JeringTech/KeyValueStore/blob/main/License.md)
[![NuGet](https://img.shields.io/nuget/vpre/Jering.KeyValueStore.svg?label=nuget)](https://www.nuget.org/packages/Jering.KeyValueStore/)

## Table of Contents
[Overview](#overview)  
[Target Frameworks](#target-frameworks)  
[Platforms](#platforms)  
[Installation](#installation)  
[Usage](#usage)  
[API](#api)  
[Performance](#performance)  
[Building and Testing](#building-and-testing)  
[Feature List](#feature-list)  
[Alternatives](#alternatives)  
[Related Concepts](#related-concepts)  
[Contributing](#contributing)  
[About](#about)  

## Overview
Jering.KeyValueStore enables you to store key-value data across memory and disk.

Usage example:

```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, string>();

// Insert
mixedStorageKVStore.Upsert(0, "dummyString1");

// Read
(Status status, string? result) = await mixedStorageKVStore.ReadAsync(0).ConfigureAwait(false);
Assert.Equal(Status.OK, status);
Assert.Equal("dummyString1", result);

// Update
mixedStorageKVStore.Upsert(0, "dummyString2");

// Verify updated
(status, result) = await mixedStorageKVStore.ReadAsync(0).ConfigureAwait(false);
Assert.Equal(Status.OK, status);
Assert.Equal("dummyString2", result);

// Delete
mixedStorageKVStore.Delete(0);

// Verify deleted
(status, result) = await mixedStorageKVStore.ReadAsync(0).ConfigureAwait(false);
Assert.Equal(Status.NOTFOUND, status);
Assert.Null(result);
```

This library is a wrapper of [Microsoft's Faster key-value store](https://github.com/microsoft/FASTER) (Faster). Faster introduces a novel, performant, lock-free concurrency system. This document 
requires a basic understanding of Faster. Refer to [Faster Basics](#faster-basics) for a quick primer as well as an overview of the features this library provides on top of Faster. 

Should I use this library over plain Faster? `MixedStorageKVStore`, isn't as optimized as a custom `FasterKV` instance could be. 
Also, at present, it only wraps a subset of Faster's features. That said, `MixedStorageKVStore`:

- [Performs well](#performance) relative to non-Faster [alternatives](#alternatives)
- Is [trivial to use](#usage)
- Exposes most Faster features that it does not wrap. This is done by allowing consumers to specify/access the underlying `FasterKV` instance (see [`MixedStorageKVSgtore.FasterKV](#TODO) and [manual Faster configuration](#advanced-configuration)).

You can use this library to begin with. Then, if/when performance bottlenecks in your applications surface, you can transition to situationally optimized `FasterKV` instances.

## Target Frameworks
- .NET Standard 2.1

## Platforms
- Windows
- macOS
- Linux
 
## Installation
Using Package Manager:
```
PM> Install-Package Jering.KeyValueStore
```
Using .Net CLI:
```
> dotnet add package Jering.KeyValueStore
```

## Usage
### Key and Value Types
`MixedStorageKVStore` keys and values can be [any type MessagePack C# can serialize](https://github.com/neuecc/MessagePack-CSharp#built-in-supported-types) (MessagePack C# is a performant 
binary serialization library used by popular libraries such as Microsoft's Signalr websockets library). Note that custom types must be annotated according 
to [MessagePack C# conventions](https://github.com/neuecc/MessagePack-CSharp#object-serialization). 

#### Common Key and Value Types
The following examples demonstrate common key and value types. 

Given class `DummyClass`:
```csharp
[MessagePackObject]
public class DummyClass
{
    [Key(0)] // Analagous to System.Text.Json.Serialization.JsonPropertyNameAttribute
    public string? DummyString { get; set; }

    [Key(1)]
    public string[]? DummyStringArray { get; set; }

    [Key(2)]
    public int DummyInt { get; set; }

    [Key(3)]
    public int[]? DummyIntArray { get; set; }
}
```
`MixedStorageKVStore` with `object` key and value types (in this example, `string` and `DummyClass` respectively):
```csharp
var mixedStorageKVStore = new MixedStorageKVStore<string, DummyClass>();
var dummyClassInstance = new DummyClass()
{
    DummyString = "dummyString",
    DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
    DummyInt = 10,
    DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
};

// Insert
mixedStorageKVStore.Upsert("dummyKey", dummyClassInstance);

// Read asynchronously
(Status status, DummyClass? result) = await mixedStorageKVStore.ReadAsync("dummyKey").ConfigureAwait(false);
Assert.Equal(Status.OK, status);
Assert.Equal(dummyClassInstance.DummyString, result?.DummyString);
Assert.Equal(dummyClassInstance.DummyStringArray, result?.DummyStringArray);
Assert.Equal(dummyClassInstance.DummyInt, result?.DummyInt);
Assert.Equal(dummyClassInstance.DummyIntArray, result?.DummyIntArray);
```
Given fixed-length-struct `DummyStruct`:
```csharp
[MessagePackObject]
public struct DummyStruct
{
    [Key(0)]
    public byte DummyByte { get; set; }

    [Key(1)]
    public short DummyShort { get; set; }

    [Key(2)]
    public int DummyInt { get; set; }

    [Key(3)]
    public long DummyLong { get; set; }
}
```
`MixedStorageKVStore` with value-type key and value types (in this example, `int` and `DummyStruct` respectively):
```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, DummyStruct>();
var dummyStructInstance = new DummyStruct()
{
    // Populate with dummy values
    DummyByte = byte.MaxValue,
    DummyShort = short.MaxValue,
    DummyInt = int.MaxValue,
    DummyLong = long.MaxValue
};

// Insert
mixedStorageKVStore.Upsert(0, dummyStructInstance);

// Read
(Status status, DummyStruct result) = await mixedStorageKVStore.ReadAsync(0).ConfigureAwait(false);
Assert.Equal(Status.OK, status);
Assert.Equal(dummyStructInstance.DummyByte, result.DummyByte);
Assert.Equal(dummyStructInstance.DummyShort, result.DummyShort);
Assert.Equal(dummyStructInstance.DummyInt, result.DummyInt);
Assert.Equal(dummyStructInstance.DummyLong, result.DummyLong);
```

#### Mutable Object Type as Key Type
Under-the-hood, the binary serialized form of what you pass as keys are the actual keys.
This means caution is required if you specify a mutable object type as your key type.
For example, consider the situation where you insert a value using a `DummyClass` (defined [above](https://github.com/JeringTech/KeyValueStore#common-key-and-value-types)) instance as key, and then change a member of the instance. 
When you try to read the value using the same instance, you either read nothing or a different value:

```csharp
var mixedStorageKVStore = new MixedStorageKVStore<DummyClass, string>();
var dummyClassInstance = new DummyClass()
{
    DummyString = "dummyString",
    DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
    DummyInt = 10,
    DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
};

// Insert
mixedStorageKVStore.Upsert(dummyClassInstance, "dummyKey");

// Read
dummyClassInstance.DummyInt = 11; // Change a member
(Status status, string? result) = await mixedStorageKVStore.ReadAsync(dummyClassInstance).ConfigureAwait(false);
Assert.Equal(Status.NOTFOUND, status); // No value for given key
Assert.Null(result);
```

We suggest avoiding mutable object types as key types.

### Concurrency
`MixedStorageKVStore.Upsert`, `MixedStorageKVStore.Delete` and `MixedStorageKVStore.ReadAsync` are thread-safe.
Example usage in highly concurrent logic:

```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, string>();
int numRecords = 100_000;

// Concurrent inserts
Parallel.For(0, numRecords, key => mixedStorageKVStore.Upsert(key, "dummyString1"));

// Concurrent reads
ConcurrentQueue<ValueTask<(Status, string?)>> pendingTasks = new();
Parallel.For(0, numRecords, key => pendingTasks.Enqueue(mixedStorageKVStore.ReadAsync(key)));
foreach (ValueTask<(Status, string?)> task in pendingTasks)
{
    // Await and verify
    Assert.Equal((Status.OK, "dummyString1"), await task.ConfigureAwait(false));
}

// Concurrent updates
Parallel.For(0, numRecords, key => mixedStorageKVStore.Upsert(key, "dummyString2"));

// Read again so we can verify updates
pendingTasks.Clear();
Parallel.For(0, numRecords, key => pendingTasks.Enqueue(mixedStorageKVStore.ReadAsync(key)));
foreach (ValueTask<(Status, string?)> task in pendingTasks)
{
    // Await and verify
    Assert.Equal((Status.OK, "dummyString2"), await task.ConfigureAwait(false));
}

// Concurrent deletes
Parallel.For(0, numRecords, key => mixedStorageKVStore.Delete(key));

// Read again so we can verify deletes
pendingTasks.Clear();
Parallel.For(0, numRecords, key => pendingTasks.Enqueue(mixedStorageKVStore.ReadAsync(key)));
foreach (ValueTask<(Status, string?)> task in pendingTasks)
{
    // Await and verify
    Assert.Equal((Status.NOTFOUND, null), await task.ConfigureAwait(false));
}
```

### Configuring `MixedStorageKVStore`
You can pass a `MixedStorageKVStoreOptions` instance to the `MixedStorageKVStore` constructor:

```csharp
var mixedStorageKVStoreOptions = new MixedStorageKVStoreOptions()
{
    // Set options
    ...
};
var mixedStorageKVStore = new MixedStorageKVStore<int, string>(mixedStorageKVStoreOptions);
```

The following table lists all options.

#### MixedStorageKVStoreOptions
| Option | Type | Description | Default |  
| ------ | ---- | ----------- | ------- |
| IndexNumBuckets | `long` | The number of buckets in Faster's index. Each bucket is 64 bits. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | 1048576 (64 MB index) |
| PageSizeBits | `int` | The size of a page in Faster's log. A page is a contiguous block of in-memory or on-disk storage. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | 25 (2^25 = 33.5 MB) |
| MemorySizeBits | `int` | The size of the in-memory region of Faster's log. If the log outgrows this region, overflow is moved to its on-disk region. Memory size must be at least two pages large. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | 26 (2^26 = 67 MB) |
| SegmentSizeBits | `int` | The size of a segment of the on-disk region of Faster's log. What is a segment? What is a segment? Records on disk are split into groups called segments. Each segment corresponds to a file. For performance reasons, segments are "pre-allocated". This means they are not created empty and left to grow gradually, instead they are created at the size specified by this value and populated gradually. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | 28 (268 MB)  |
| LogDirectory | `string` | The directory containing the on-disk region of Faster's log. If this value is `null`, whitespace or an empty string, log files are placed in "&lt;temporary path&gt;/FasterLogs" where "&lt;temporary path&gt;" is the value returned by `Path.GetTempPath`. Note that nothing is written to disk while your log fits in-memory. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | `null`  |
| LogFileNamePrefix | `string` | The Faster-log filename prefix. The on-disk region of the log is stored across multiple files. Each file is referred to as a segment. Each segment has file name "&lt;log file name prefix&gt;.log.&lt;segment number&gt;". If this value is `null`, whitespace or an empty string, a random `Guid` is used as the prefix. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | `null`  |
| TimeBetweenLogCompactionsMS | `int` | The time between Faster-log compaction attempts. If this value is negative, log compaction is disabled. | `60000`  |
| InitialLogCompactionThresholdBytes | `long` | The initial log compaction threshold. Initially, log compactions only run when the Faster-log's safe-readonly region's size is larger than or equal to this value. If log compactions run 5 times in a row, this value is doubled. Why? Consider the situation where the safe-readonly region is already compact, but still larger than the threshold. Not increasing the threshold would result in continual redundant compaction runs. If this value is less than or equal to 0, the initial log compaction threshold is 2 * memory size (`MemorySizeBits`). | `0`  |
| DeleteLogOnClose | `bool` | The value specifying whether Faster-log files are deleted when the `MixedStorageKVStore` is disposed or finalized (at which points underlying Faster-log files are closed). This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | `true` |
| MessagePackSerializerOptions | `MessagePackSerializerOptions` | The options for serializing data using MessagePack C#. MessagePack C# is an efficient binary serialization library. Refer to [MessagePack C# documentation](https://github.com/neuecc/MessagePack-CSharp) for details. | [Standard options with Lz4BlockArray compression](https://github.com/neuecc/MessagePack-CSharp#lz4-compression) |

#### Advanced Configuration
If you'd like greater control over Faster, you can pass a manually configured `FasterKV<SpanByte, SpanByte>` instance to the `MixedStorageKVStore` constructor:

```csharp
var logSettings = new LogSettings()
{
    // Set options
    ...
};
var fasterKV = new FasterKV<SpanByte, SpanByte>(1L << 20, logSettings));
var mixedStorageKVStoreOptions = new MixedStorageKVStoreOptions()
{
    // Set options
    ...
};
var mixedStorageKVStore = new MixedStorageKVStore<int, string>(mixedStorageKVStoreOptions, fasterKVStore: fasterKV);
```

### On-Disk Data
Basics:

- When is data written to disk? `MixedStorageKVStore` stores data across memory and disk. Data is written to disk when
the in-memory region of your store is full. You can configure the size of the in-memory region using 
[`MixedStorageKVStoreOptions.MemorySizeBits`](TODO).

- Where is on-disk data located? By default, `<temp path>/FasterLogs`, where <temp path> is the value returned by `Path.GetTempPath`. 
You can change this directory using [`MixedStorageKVStoreOptions.LogDirectory`](TODO).

The following example generates files that you can examine:

```csharp
var mixedStorageKVStoreOptions = new MixedStorageKVStoreOptions()
{
    PageSizeBits = 12, // See MixedStorageKVStoreOptions.PageSizeBits in API section below
    MemorySizeBits = 13,
    DeleteLogOnClose = false // Disables automatic deleting of files on disk. See MixedStorageKVStoreOptions.DeleteLogOnClose in API section below
};
var mixedStorageKVStore = new MixedStorageKVStore<int, string>(mixedStorageKVStoreOptions);

// Insert
Parallel.For(0, 100_000, key => mixedStorageKVStore.Upsert(key, "dummyString1"));
```

You will find a file in `<temp path>/FasterLogs` named `<guid>.log.0`. An example absolute filepath on windows might look like 
`C:/Users/UserName/AppData/Local/Temp/FasterLogs/836b4239-ab56-4fa8-b3a5-833cbd198044.log.0`.

#### Managing Files
By default, files are deleted on `MixedStorageKVStore` disposal or finalization.
If your program terminates abruptly, files may not get deleted.
We suggest:

- Placing all files in the same directory. Do this by specifying the same [`MixedStorageKVStoreOptions.LogDirectory`](TODO) for all `MixedStorageKVStore`s. This is the default behaviour -
  all files are placed in `<temp path>/FasterLogs`.
- On application initialization, if the directory exists, delete it.
    ```csharp
    try
    {
        Directory.Delete(Path.Combine(Path.GetTempPath(), "FasterLogs"), true);
    }
    catch
    {
        // Do nothing
    }
    ```

#### Managing Disk Space
While `MixedStorageKVStore` performs [log compaction](log-compaction) periodically, data can only be so compact. So long as you're adding new records, 
the size of your data can grow boundlessly. Therefore, we recommend monitoring disk space the same way you would monitor memory or CPU usage. For example, if you're 
using a cloud VM, consider setting an alert for when disk space usage reaches a certain percentage.

## API
### MixedStorageKVStore<TKey, TValue>
#### Constructor
##### Signature
```csharp
public MixedStorageKVStore(MixedStorageKVStoreOptions? mixedStorageKVStoreOptions = null,
    ILogger<MixedStorageKVStore<TKey, TValue>>? logger = null,
    FasterKV<SpanByte, SpanByte>? fasterKVStore = null)
```
##### Description
Creates a `MixedStorageKVStore<TKey, TValue>`.
##### Parameters
- `mixedStorageKVStoreOptions`
  - Type: `MixedStorageKVStoreOptions`
  - Description: The options for the `MixedStorageKVStore<TKey, TValue>`.
- `logger`
  - Type: `ILogger<MixedStorageKVStore<TKey, TValue>>`
  - Description: The logger for log compaction events.
- `fasterKVStore`
  - Type: `FasterKV<SpanByte, SpanByte>`
  - Description: The underlying `FasterKV<SpanByte, SpanByte>` for the `MixedStorageKVStore<TKey, TValue>`. Specify this value if you want to manually configure it.

#### Properties
##### `FasterKV`
###### Signature
```csharp
FasterKV<SpanByte, SpanByte> FasterKV { get; }
```
###### Description
Gets the underlying `FasterKV<SpanByte, SpanByte>` instance.

#### Methods
##### `Upsert`
###### Signature
```csharp
void Upsert(TKey key, TValue obj);
```
###### Description
Updates or inserts a record.
###### Parameters
- `key`
  - Type: `TKey`
  - Description: The key of the record.
- `obj`
  - Type: `TValue`
  - Description: The new value of the record.
###### Exceptions
- `ObjectDisposedException`
  - Thrown if the instance or a dependency is disposed.

##### `Delete`
###### Signature
```csharp
Status Delete(TKey key);
```
###### Description
Deletes a record.
###### Parameters
- `key`
  - Type: `TKey`
  - Description: The key of the record to delete.
######  Exceptions
- `ObjectDisposedException`
  - Thrown if the instance or a dependency is disposed.

##### `ReadAsync`
###### Signature
```csharp
ValueTask<(Status, TValue?)> ReadAsync(TKey key);
```
###### Description
Reads a record asynchronously.
###### Parameters
- `key`
  - Type: `TKey`
  - Description: The key of the record to read.
###### Returns
The task representing the asynchronous operation.
###### Exceptions
- `ObjectDisposedException`
  - Thrown if the instance or a dependency is disposed.

## Performance
### Benchmarks
The following benchmarks use `MixedStorageKVStore`s with key type `int` and value type `DummyClass` as defined and populated in [this section](#common-key-and-value-types).  

`MixedStorageKVStore`s are configured to provide basis for comparison with disk-based alternatives like Sqlite and LiteDB:

- MessagePack C# compression is disabled 
- The vast majority of the store is on-disk (8 KB in-memory region, multi-MB on-disk region)
- Log compaction is disabled

Benchmarks:

- Inserts_WithoutCompression performs 350,000 single-record insertions per iteration.  
- Reads_WithoutCompression performs 75,000 single-record reads per iteration.

View source [here](https://github.com/JeringTech/KeyValueStore/blob/main/perf/KeyValueStore/LowMemoryUsageBenchmarks.cs).

Results:

|                     Method |       Mean |    Error |   StdDev |      Gen 0 |      Gen 1 | Gen 2 | Allocated |
|--------------------------- |-----------:|---------:|---------:|-----------:|-----------:|------:|----------:|
| Inserts_WithoutCompression |   552.1 ms | 15.86 ms | 46.77 ms | 25000.0000 |          - |     - |  98.57 MB |
|   Reads_WithoutCompression | 1,304.2 ms | 29.59 ms | 85.85 ms | 40000.0000 | 13000.0000 |     - | 156.72 MB |

``` ini

BenchmarkDotNet=v0.12.1, OS=Windows 10.0.19041.804 (2004/?/20H1)
Intel Core i7-7700 CPU 3.60GHz (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
Samsung NVMe M.2 SSD 960 EVO 250GB
.NET Core SDK=5.0.200
  [Host]     : .NET Core 5.0.3 (CoreCLR 5.0.321.7212, CoreFX 5.0.321.7212), X64 RyuJIT
  Job-XFZMWA : .NET Core 5.0.3 (CoreCLR 5.0.321.7212, CoreFX 5.0.321.7212), X64 RyuJIT

InvocationCount=1  UnrollFactor=1  

```

#### Cursory analysis
Insert performance is extraordinary. Per-second insertion rate beats disk-based alternatives by an order of magnitude or more in our internal benchmarks.  

Read performance is good - close to the fastest disk-based alternatives. One thing to note is that unlike alternatives,
Faster does not lock while writing. If we mix reads and writes, `MixedStorageKeyValueStore` reads could outperform alternatives.

More benchmarks are required for memory-based situations and different key/value types. Benchmarks for alternatives should be added as well.

### Future Performance Improvements
Performance of the current `MixedStorageKeyValueStore` implementation exceeds our requirements. That said, we're open to pull-requests improving performance.
Several low-hanging fruit to consider:

- Support [Faster's read only cache](https://microsoft.github.io/FASTER/docs/fasterkv-tuning/#configuring-the-read-cache). This cache is an in-memory 
  cache of recently read records. Depending on read-patterns, this could reduce average read latency significantly.

- Fast-path for blittable types: [Blittable types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types) are fixed-length value-types.
  Instances of these types can be converted to their binary forms without going through MessagePack C#. Also they are fixed-length and so do not need `SpanByte` wrappers. 
  We ran internal benchmarks for a `MixedStorageKeyValueStore<int, string>` store with a fast path for its `int` (a blittable type) keys.
  Read performance improved by ~20%. 

- Use object log for mostly-in-memory situations. 

## Building and Testing
You can build and test this project in Visual Studio 2019.

## Alternatives
Apart from plain Faster, consider the following alternatives:
<!-- TODO pros/cons -->

- [ManagedEsent `PersistentDictionary`](https://github.com/microsoft/ManagedEsent/blob/master/Documentation/PersistentDictionaryDocumentation.md)

- [SQLite](https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/?tabs=netcore-cli)

- [LiteDB](https://www.litedb.org/)

- [MemoryCache](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.caching.memorycache?view=dotnet-plat-ext-5.0)

## Related Concepts
### Faster Basics
This section provides just enough information about Faster to use *this* library effectively. Refer to the 
[official Faster documentation](https://microsoft.github.io/FASTER/docs/fasterkv-basics/) for complete information on Faster.  

Faster is a key-value store library. `FasterKV` is the key-value store type Faster exposes. 
A `FasterKV` instance is composed of an **index** and a **log**.  

#### Index
The index is a simple [hash table](https://en.wikipedia.org/wiki/Hash_table) that maps keys to locations in the log.

#### Log
You can think of the log as a list of key-value pairs (records). For example, say we insert 3 records with keys 0, 1, and 2 and value "dummyString1". We insert them in order
of increasing key value. Our log will look like this:

```
// Head
key: 0, value: "dummyString1"
key: 1, value: "dummyString1"
key: 2, value: "dummyString1"
// Tail - records are added here
```

Mutiple records can have the same key. Say we update values to "dummyString2", our log will now look like this:

```
// Head
key: 0, value: "dummyString1"
key: 1, value: "dummyString1"
key: 2, value: "dummyString1"
// Index points to these - the most recent records for each key
key: 0, value: "dummyString2"
key: 1, value: "dummyString2"
key: 2, value: "dummyString2"
// Tail
```

**Log compaction** removes redundant records. After log compaction:

```
key: 0, value: "dummyString2"
key: 1, value: "dummyString2"
key: 2, value: "dummyString2"
```

By default, `MixedStorageKeyValueStore` performs periodic log compactions for you.  

The log can span memory and disk. Say we configure the in-memory region to fit 3 records. If we have 6 records, 3 end up on disk:

```
// In-memory region
key: 0, value: "dummyString2"
key: 1, value: "dummyString2"
key: 2, value: "dummyString2"
// On-disk region
key: 3, value: "dummyString2"
key: 4, value: "dummyString2"
key: 5, value: "dummyString2"
```

Records on disk are split into groups called **segments**. Each segment corresponds to a fixed-size file. Say we configure segments to fit 3 records.
If we have 4 records, the on-disk region of our log will look like this:

```
// On-disk region
// Segment 0, full
key: 3, value: "dummyString2"
key: 4, value: "dummyString2"
key: 5, value: "dummyString2"
// Segment 1, partially filled
key: 6, value: "dummyString2"
empty
empty
```

`MixedStorageKeyValueStore` has Faster "pre-allocate" files to speeds up inserts. This means files are not created empty and left to grow gradually, 
instead they are created at the segment size of our choosing and populated gradually.  

The log is also subdivided into **pages**. Pages are a separate concept from segments - segments typically consist of multiple pages -
pages are contiguous blocks of in-memory or on-disk storage. What are pages for? Records are moved around as pages. For example, when we add records, 
they are held in memory and only written to the log after we've added enough to fill a page.

Note: Both segments and pages affect Faster in ways that aren't relevant to the current `MixedStorageKeyValueStore` implementation. For example, 
segment size affects truncation granularity, but `MixedStorageKeyValueStore` does not support truncation. Refer to the official Faster documentation
to learn more about these concepts.

#### Sessions
Most interactions with a `FasterKV` instance are done through sessions. A session's members are not thread safe:

```csharp
// Create FasterKV instance with least possible configuration
FasterKV<int, string> fasterKV = new(1L << 20, logSettings);

// Create a session
var session = fasterKV.For(simpleFunctions).NewSession<SimpleFunctions<int, string>>(); // simpleFunction is an instance of SimpleFunctions<int, string>

// Perform operations
session.Upsert(0, "dummyString"); // Not thread-safe, so you need to manage a pool of sessions for multi-threaded logic
```

`MixedStorageKeyValueStore` abstracts sessions away:  
```csharp
// Create MixedStorageKeyValueStore instance
MixedStorageKeyValueStore<int, string> mixedStorageKeyValueStore = new();

// Perform operations
mixedStorageKeyValueStore.Upsert(); // Thread-safe
```

#### Serialization
Faster differentiates between fixed-length types (primitives, structs with only primitive members etc),
and variable-length types (objects like strings, custom classes etc).

By default, `FasterKV` serializes variable-length types using [`DataContractSerializer`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.datacontractserializer?view=net-5.0),
a slow, space-inefficient XML serializer. It then writes serialized variable-length data to a secondary log, the "object log".
Use of the object log significantly slows down both writes and reads.

To avoid the object log, an alternative system was added around the `SpanByte` struct. 
`SpanByte` can be thought of as a wrapper for "serialized variable-length data" + "integer specifying the data's length".
If a `FasterKV` instance is instantiated with `Spanbyte` in place of variable length types, for example, `FasterKV<int, SpanByte>`
instead of `FasterKV<int, DummyClass>`, *all data* is written to the primary log.  

While this avoids the object log, it means you need to manually serialize variable-length types and instantiate `SpanByte`s around the resultant data.  

`MixedStorageKeyValueStore` abstracts all that away: 
```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, DummyClass>();

// Upsert updates or inserts records.
//
// Under-the-hood, MixedStorageKeyValueStore serializes dummyClassInstance using the MessagePack C# library, 
// creates a `SpanByte` around the resultant data, and passes the `SpanByte` to the underlying FasterKV instance.
mixedStorageKVStore.Upsert(0, dummyClassInstance); 
```

Note: The object log is not a great general solution, but it is faster if most of the log is in memory. This is because it allows for "in-place" updates within the 
in-memory region of the log. Supporting object log for such situations is listed under [Future Performance Improvements](#future-performance-improvements).

## Contributing
Contributions are welcome!

### Contributors
- [JeremyTCD](https://github.com/JeremyTCD)

Thanks to [badrishc](https://github.com/badrishc) for help getting started with Faster. Quite a bit of this library is based
on his suggestions.

## About
Follow [@JeringTech](https://twitter.com/JeringTech) for updates and more.
