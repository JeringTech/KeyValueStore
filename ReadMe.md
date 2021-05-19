# Jering.KeyValueStore
[![Build Status](https://dev.azure.com/JeringTech/KeyValueStore/_apis/build/status/JeringTech.KeyValueStore?repoName=JeringTech%2FKeyValueStore&branchName=refs%2Fpull%2F2%2Fmerge)](https://dev.azure.com/JeringTech/KeyValueStore/_build/latest?definitionId=11&repoName=JeringTech%2FKeyValueStore&branchName=refs%2Fpull%2F2%2Fmerge)
[![codecov](https://codecov.io/gh/JeringTech/KeyValueStore/branch/main/graph/badge.svg?token=5STAAJJ4Q4)](https://codecov.io/gh/JeringTech/KeyValueStore)
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
[Alternatives](#alternatives)  
[Related Concepts](#related-concepts)  
[Contributing](#contributing)  
[About](#about)  

## Overview
Jering.KeyValueStore enables you to store key-value data across memory and disk.

Usage:

```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, string>(); // Stores data across memory (primary storage) and disk (secondary storage)

// Insert
await mixedStorageKVStore.UpsertAsync(0, "dummyString1").ConfigureAwait(false); // Insert a key-value pair (record)

// Verify inserted
(Status status, string? result) = await mixedStorageKVStore.ReadAsync(0).ConfigureAwait(false);
Assert.Equal(Status.OK, status); // Status.NOTFOUND if no record with key 0
Assert.Equal("dummyString1", result);

// Update
await mixedStorageKVStore.UpsertAsync(0, "dummyString2").ConfigureAwait(false);

// Verify updated
(status, result) = await mixedStorageKVStore.ReadAsync(0).ConfigureAwait(false);
Assert.Equal(Status.OK, status);
Assert.Equal("dummyString2", result);

// Delete
await mixedStorageKVStore.DeleteAsync(0).ConfigureAwait(false);

// Verify deleted
(status, result) = await mixedStorageKVStore.ReadAsync(0).ConfigureAwait(false);
Assert.Equal(Status.NOTFOUND, status);
Assert.Null(result);
```

This library is a wrapper of Microsoft's [Faster key-value store](https://github.com/microsoft/FASTER). Faster is a low-level key-value store that introduces a novel, lock-free concurrency system. 
You'll need a basic understanding of Faster to use this library. Refer to [Faster Basics](#faster-basics) for a quick primer and an overview of features this library provides on top of Faster. 

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
This section explains how to use this library. Topics:

[Choosing Key and Value Types](#choosing-key-and-value-types)  
[Using This Library in Highly Concurrent Logic](#using-this-library-in-highly-concurrent-logic)  
[Configuring](#configuring)  
[Creating and Managing On-Disk Data](#creating-and-managing-on-disk-data)

### Choosing Key and Value Types
[MessagePack C#](https://github.com/neuecc/MessagePack-CSharp) must be able to serialize your `MixedStorageKVStore` key and value types.  

The list of types MessagePack C# can serialize includes [built-in types](https://github.com/neuecc/MessagePack-CSharp#built-in-supported-types) and custom types annotated according to 
[MessagePack C# conventions](https://github.com/neuecc/MessagePack-CSharp#object-serialization).  

#### Common Key and Value Types
The following are examples of common key and value types. 

##### Reference Types
The following custom reference type is annotated according to MessagePack C# conventions:

```csharp
[MessagePackObject] // MessagePack C# attribute
public class DummyClass
{
    [Key(0)] // MessagePack C# attribute
    public string? DummyString { get; set; }

    [Key(1)]
    public string[]? DummyStringArray { get; set; }

    [Key(2)]
    public int DummyInt { get; set; }

    [Key(3)]
    public int[]? DummyIntArray { get; set; }
}
```
We can use it, together with the built-in reference type `string` as key and value types:
```csharp
var mixedStorageKVStore = new MixedStorageKVStore<string, DummyClass>(); // string key, DummyClass value
var dummyClassInstance = new DummyClass()
{
    DummyString = "dummyString",
    DummyStringArray = new[] { "dummyString1", "dummyString2", "dummyString3", "dummyString4", "dummyString5" },
    DummyInt = 10,
    DummyIntArray = new[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000 }
};

// Insert
await mixedStorageKVStore.UpsertAsync("dummyKey", dummyClassInstance).ConfigureAwait(false);

// Read
(Status status, DummyClass? result) = await mixedStorageKVStore.ReadAsync("dummyKey").ConfigureAwait(false);

// Verify
Assert.Equal(Status.OK, status);
Assert.Equal(dummyClassInstance.DummyString, result!.DummyString); // result is only null if status is Status.NOTFOUND
Assert.Equal(dummyClassInstance.DummyStringArray, result!.DummyStringArray);
Assert.Equal(dummyClassInstance.DummyInt, result!.DummyInt);
Assert.Equal(dummyClassInstance.DummyIntArray, result!.DummyIntArray);
```
##### Value Types
The following custom value-type is annotated according to MessagePack C# conventions:
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
We can use it, together with the built-in value type `int` as key and value types:
```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, DummyStruct>(); // int key, DummyStruct value
var dummyStructInstance = new DummyStruct()
{
    // Populate with dummy values
    DummyByte = byte.MaxValue,
    DummyShort = short.MaxValue,
    DummyInt = int.MaxValue,
    DummyLong = long.MaxValue
};

// Insert
await mixedStorageKVStore.UpsertAsync(0, dummyStructInstance).ConfigureAwait(false);

// Read
(Status status, DummyStruct result) = await mixedStorageKVStore.ReadAsync(0).ConfigureAwait(false);

// Verify
Assert.Equal(Status.OK, status);
Assert.Equal(dummyStructInstance.DummyByte, result.DummyByte);
Assert.Equal(dummyStructInstance.DummyShort, result.DummyShort);
Assert.Equal(dummyStructInstance.DummyInt, result.DummyInt);
Assert.Equal(dummyStructInstance.DummyLong, result.DummyLong);
```

#### Mutable Type as Key Type
Before we conclude this section on key and value types, a word of caution on using mutable types (type with members you can modify after creation)
as key types:  

Under-the-hood, the binary serialized form of what you pass as keys are the actual keys.
This means that if you pass an instance of a mutable type as a key, then modify a member, you can no longer use it retrieve the original record.  

For example, consider the situation where you insert a value using a `DummyClass` instance (defined [above](#common-key-and-value-types)) as key, and then change a member of the instance. 
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
await mixedStorageKVStore.UpsertAsync(dummyClassInstance, "dummyKey").ConfigureAwait(false);

// Read
dummyClassInstance.DummyInt = 11; // Change a member
(Status status, string? result) = await mixedStorageKVStore.ReadAsync(dummyClassInstance).ConfigureAwait(false);

// Verify
Assert.Equal(Status.NOTFOUND, status); // No value for given key
Assert.Null(result);
```

We suggest avoiding mutable object types as key types.

### Using This Library in Highly Concurrent Logic
`MixedStorageKVStore.UpsertAsync`, `MixedStorageKVStore.DeleteAsync` and `MixedStorageKVStore.ReadAsync` are thread-safe and suitable for highly concurrent situations
situations. Some example usage:

```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, string>();
int numRecords = 100_000;

// Concurrent inserts
ConcurrentQueue<Task> upsertTasks = new();
Parallel.For(0, numRecords, key => upsertTasks.Enqueue(mixedStorageKVStore.UpsertAsync(key, "dummyString1")));
await Task.WhenAll(upsertTasks).ConfigureAwait(false);

// Concurrent reads
ConcurrentQueue<ValueTask<(Status, string?)>> readTasks = new();
Parallel.For(0, numRecords, key => readTasks.Enqueue(mixedStorageKVStore.ReadAsync(key)));
foreach (ValueTask<(Status, string?)> task in readTasks)
{
    // Verify
    Assert.Equal((Status.OK, "dummyString1"), await task.ConfigureAwait(false));
}

// Concurrent updates
upsertTasks.Clear();
Parallel.For(0, numRecords, key => upsertTasks.Enqueue(mixedStorageKVStore.UpsertAsync(key, "dummyString2")));
await Task.WhenAll(upsertTasks).ConfigureAwait(false);

// Read again so we can verify updates
readTasks.Clear();
Parallel.For(0, numRecords, key => readTasks.Enqueue(mixedStorageKVStore.ReadAsync(key)));
foreach (ValueTask<(Status, string?)> task in readTasks)
{
    // Verify
    Assert.Equal((Status.OK, "dummyString2"), await task.ConfigureAwait(false));
}

// Concurrent deletes
ConcurrentQueue<ValueTask<Status>> deleteTasks = new();
Parallel.For(0, numRecords, key => deleteTasks.Enqueue(mixedStorageKVStore.DeleteAsync(key)));
foreach (ValueTask<Status> task in deleteTasks)
{
    Status result = await task.ConfigureAwait(false);

    // Verify
    Assert.Equal(Status.OK, result);
}

// Read again so we can verify deletes
readTasks.Clear();
Parallel.For(0, numRecords, key => readTasks.Enqueue(mixedStorageKVStore.ReadAsync(key)));
foreach (ValueTask<(Status, string?)> task in readTasks)
{
    // Verify
    Assert.Equal((Status.NOTFOUND, null), await task.ConfigureAwait(false));
}
```

### Configuring
To configure a `MixedStorageKVStore`, pass it a `MixedStorageKVStoreOptions` instance:

```csharp
var mixedStorageKVStoreOptions = new MixedStorageKVStoreOptions()
{
    // Specify options
    LogDirectory = "my/log/directory",
    ...
};
var mixedStorageKVStore = new MixedStorageKVStore<int, string>(mixedStorageKVStoreOptions);
```

We've listed all of the options in the API section: [`MixedStorageKVStoreOptions`](#mixedstoragekvstoreoptions-class).

#### Advanced Configuration
If you want greater control over faster, you can pass a manually configured `FasterKV<SpanByte, SpanByte>` instance to `MixedStorageKVStore`:

```csharp
var logSettings = new LogSettings() // Faster options type
{
    // Specify options
    ...
};
var fasterKV = new FasterKV<SpanByte, SpanByte>(1L << 20, logSettings)); // Manually configured FasterKV
var mixedStorageKVStoreOptions = new MixedStorageKVStoreOptions()
{
    // Specify options
    LogDirectory = "my/log/directory",
    ...
};
var mixedStorageKVStore = new MixedStorageKVStore<int, string>(mixedStorageKVStoreOptions, fasterKVStore: fasterKV);
```

### Creating and Managing On-Disk Data
`MixedStorageKVStore` stores data across memory and disk. This section briefly covers on-disk data.

- When is data written to disk? `MixedStorageKVStore` writes to disk when
the in-memory region of your store is full. You can configure the size of the in-memory region using 
[`MixedStorageKVStoreOptions.MemorySizeBits`](#MixedStorageKVStoreOptionsMemorySizeBits).

- Where is on-disk data located? By default, it is located in `<temp path>/FasterLogs`, where `<temp path>` is the value returned by `Path.GetTempPath()`. 
You can specify `<temp path>` using [`MixedStorageKVStoreOptions.LogDirectory`](#MixedStorageKVStoreOptionsLogDirectory).

- Can I recreate a `MixedStorageKVStore` from on-disk data? You can do this using Faster's checkpointing system. This library
doesn't wrap the system, so you'll have to do it manually.

The following example writes data to disk:

```csharp
var mixedStorageKVStoreOptions = new MixedStorageKVStoreOptions()
{
    PageSizeBits = 12, // See MixedStorageKVStoreOptions.PageSizeBits in the MixedStorageKVStoreOptions section above
    MemorySizeBits = 13,
    DeleteLogOnClose = false // Disables automatic deleting of files on disk. See MixedStorageKVStoreOptions.DeleteLogOnClose in the MixedStorageKVStoreOptions section above
};
var mixedStorageKVStore = new MixedStorageKVStore<int, string>(mixedStorageKVStoreOptions);

// Insert
ConcurrentQueue<Task> upsertTasks = new();
Parallel.For(0, 100_000, key => upsertTasks.Enqueue(mixedStorageKVStore.UpsertAsync(key, "dummyString1")));
await Task.WhenAll(upsertTasks).ConfigureAwait(false);
```

You will find a file in `<temp path>/FasterLogs` named `<guid>.log.0`. An example absolute filepath on windows might look like 
`C:/Users/UserName/AppData/Local/Temp/FasterLogs/836b4239-ab56-4fa8-b3a5-833cbd198044.log.0`.

#### Managing Files
By default, `MixedStorageKVStore` deletes files on disposal or finalization.
If your program terminates abruptly, `MixedStorageKVStore` may not delete files.
We suggest:

- Placing all files in the same directory. Do this by specifying the same [`MixedStorageKVStoreOptions.LogDirectory`](#MixedStorageKVStoreOptionsLogDirectory) for all `MixedStorageKVStore`s. This is the default behaviour:
  all files are placed in `<temp path>/FasterLogs`.
- On application initialization, delete the directory if it exists:
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
`MixedStorageKVStore` performs log compaction periodically, however, data can only be so compact - the size of your data can grow boundlessly as long as you're adding new records. 
Therefore, we recommend monitoring disk space the same way you would monitor any other metric.

## API
<!-- MixedStorageKVStore generated docs -->

### MixedStorageKVStore<TKey, TValue> Class
#### Constructors
##### MixedStorageKVStore(MixedStorageKVStoreOptions, ILogger<MixedStorageKVStore<TKey, TValue>>, FasterKV<SpanByte, SpanByte>)

Creates a `MixedStorageKVStore<TKey, TValue>`.
```csharp
public MixedStorageKVStore([MixedStorageKVStoreOptions? mixedStorageKVStoreOptions = null], [ILogger<MixedStorageKVStore<TKey, TValue>>? logger = null], [FasterKV<SpanByte, SpanByte>? fasterKVStore = null])
```
###### Parameters
mixedStorageKVStoreOptions `MixedStorageKVStoreOptions`  
The options for the `MixedStorageKVStore<TKey, TValue>`.

logger `ILogger<MixedStorageKVStore<TKey, TValue>>`  
The logger for log compaction events.

fasterKVStore `FasterKV<SpanByte, SpanByte>`  
The underlying `FasterKV<Key, Value>` for the `MixedStorageKVStore<TKey, TValue>`.
This parameter allows you to use a manually configured Faster instance.
#### Properties
##### MixedStorageKVStore<TKey, TValue>.FasterKV

Gets the underlying `FasterKV<Key, Value>` instance.
```csharp
public FasterKV<SpanByte, SpanByte> FasterKV { get; }
```
#### Methods
##### MixedStorageKVStore<TKey, TValue>.UpsertAsync(TKey, TValue)

Updates or inserts a record asynchronously.
```csharp
public Task UpsertAsync(TKey key, TValue obj)
```
###### Parameters
key `TKey`  
The record's key.

obj `TValue`  
The record's new value.
###### Returns
The task representing the asynchronous operation.
###### Exceptions
`ObjectDisposedException`  
Thrown if the instance or a dependency is disposed.
###### Remarks
This method is thread-safe.
##### MixedStorageKVStore<TKey, TValue>.DeleteAsync(TKey)

Deletes a record asynchronously.
```csharp
public ValueTask<Status> DeleteAsync(TKey key)
```
###### Parameters
key `TKey`  
The record's key.
###### Returns
The task representing the asynchronous operation.
###### Exceptions
`ObjectDisposedException`  
Thrown if the instance or a dependency is disposed.
###### Remarks
This method is thread-safe.
##### MixedStorageKVStore<TKey, TValue>.ReadAsync(TKey)

Reads a record asynchronously.
```csharp
public ValueTask<(Status, TValue?)> ReadAsync(TKey key)
```
###### Parameters
key `TKey`  
The record's key.
###### Returns
The task representing the asynchronous operation.
###### Exceptions
`ObjectDisposedException`  
Thrown if the instance or a dependency is disposed.
###### Remarks
This method is thread-safe.
##### MixedStorageKVStore<TKey, TValue>.Dispose()

Disposes this instance.
```csharp
public void Dispose()
```
<!-- MixedStorageKVStore generated docs -->

<!-- MixedStorageKVStoreOptions generated docs -->

### MixedStorageKVStoreOptions Class
#### Constructors
##### MixedStorageKVStoreOptions()
```csharp
public MixedStorageKVStoreOptions()
```
#### Properties
##### MixedStorageKVStoreOptions.IndexNumBuckets
The number of buckets in Faster's index.
```csharp
public long IndexNumBuckets { get; set; }
```
###### Remarks
Each bucket is 64 bits.  

This value is ignored if a `FasterKV<Key, Value>` instance is supplied to the `MixedStorageKVStore<TKey, TValue>` constructor.  

Defaults to 1048576 (64 MB index).
##### MixedStorageKVStoreOptions.PageSizeBits
The size of a page in Faster's log.
```csharp
public int PageSizeBits { get; set; }
```
###### Remarks
A page is a contiguous block of in-memory or on-disk storage.  

This value is ignored if a `FasterKV<Key, Value>` instance is supplied to the `MixedStorageKVStore<TKey, TValue>` constructor.  

Defaults to 25 (2^25 = 33.5 MB).
##### MixedStorageKVStoreOptions.MemorySizeBits
The size of the in-memory region of Faster's log.
```csharp
public int MemorySizeBits { get; set; }
```
###### Remarks
If the log outgrows this region, overflow is moved to its on-disk region.  

This value is ignored if a `FasterKV<Key, Value>` instance is supplied to the `MixedStorageKVStore<TKey, TValue>` constructor.  

Defaults to 26 (2^26 = 67 MB).
##### MixedStorageKVStoreOptions.SegmentSizeBits
The size of a segment of the on-disk region of Faster's log.
```csharp
public int SegmentSizeBits { get; set; }
```
###### Remarks
What is a segment? Records on disk are split into groups called segments. Each segment corresponds to a file.  

For performance reasons, segments are "pre-allocated". This means they are not created empty and left to grow gradually, instead they are created at the size specified by this value and populated gradually.  

This value is ignored if a `FasterKV<Key, Value>` instance is supplied to the `MixedStorageKVStore<TKey, TValue>` constructor.  

Defaults to 28 (268 MB).
##### MixedStorageKVStoreOptions.LogDirectory
The directory containing the on-disk region of Faster's log.
```csharp
public string? LogDirectory { get; set; }
```
###### Remarks
If this value is `null` or an empty string, log files are placed in "&lt;temporary path&gt;/FasterLogs" where 
"&lt;temporary path&gt;" is the value returned by `Path.GetTempPath`.  

Note that nothing is written to disk while your log fits in-memory.  

This value is ignored if a `FasterKV<Key, Value>` instance is supplied to the `MixedStorageKVStore<TKey, TValue>` constructor.  

Defaults to `null`.
##### MixedStorageKVStoreOptions.LogFileNamePrefix
The Faster log filename prefix.
```csharp
public string? LogFileNamePrefix { get; set; }
```
###### Remarks
The on-disk region of the log is stored across multiple files. Each file is referred to as a segment.
Each segment has file name "&lt;log file name prefix&gt;.log.&lt;segment number&gt;".
  

If this value is `null` or an empty string, a random `Guid` is used as the prefix.  

This value is ignored if a `FasterKV<Key, Value>` instance is supplied to the `MixedStorageKVStore<TKey, TValue>` constructor.  

Defaults to `null`.
##### MixedStorageKVStoreOptions.TimeBetweenLogCompactionsMS
The time between Faster log compaction attempts.
```csharp
public int TimeBetweenLogCompactionsMS { get; set; }
```
###### Remarks
If this value is negative, log compaction is disabled.  

Defaults to 60000.
##### MixedStorageKVStoreOptions.InitialLogCompactionThresholdBytes
The initial log compaction threshold.
```csharp
public long InitialLogCompactionThresholdBytes { get; internal set; }
```
###### Remarks
Initially, log compactions only run when the Faster log's safe-readonly region's size is larger than or equal to this value.  

If log compaction runs 5 times in a row, this value is doubled. Why? Consider the situation where the safe-readonly region is already 
compact, but still larger than the threshold. Not increasing the threshold would result in redundant compaction runs.  

If this value is less than or equal to 0, the initial log compaction threshold is 2 * memory size in bytes (`MixedStorageKVStoreOptions.MemorySizeBits`).  

Defaults to 0.
##### MixedStorageKVStoreOptions.DeleteLogOnClose
The value specifying whether log files are deleted when the `MixedStorageKVStore<TKey, TValue>` is disposed or finalized (at which points underlying log files are closed).
```csharp
public bool DeleteLogOnClose { get; set; }
```
###### Remarks
This value is ignored if a `FasterKV<Key, Value>` instance is supplied to the `MixedStorageKVStore<TKey, TValue>` constructor.  

Defaults to `true`.
##### MixedStorageKVStoreOptions.MessagePackSerializerOptions
The options for serializing data using MessagePack C#.
```csharp
public MessagePackSerializerOptions MessagePackSerializerOptions { get; set; }
```
###### Remarks
MessagePack C# is a performant binary serialization library. Refer to [MessagePack C# documentation](https://github.com/neuecc/MessagePack-CSharp)
for details.  

Defaults to `MessagePackSerializerOptions.Standard` with compression using `MessagePackCompression.Lz4BlockArray`.
<!-- MixedStorageKVStoreOptions generated docs -->

## Performance
### Benchmarks
The following benchmarks use `MixedStorageKVStore`s with key type `int` and value type `DummyClass` as defined and populated in [this section](#common-key-and-value-types).  
The `MixedStorageKVStore`s are configured to provide basis for comparison with disk-based alternatives like Sqlite and LiteDB:

- MessagePack C# compression is disabled 
- The vast majority of the store is on-disk (8 KB in-memory region, multi-MB on-disk region)
- Log compaction is disabled

Benchmarks:

- Inserts_WithoutCompression performs 350,000 single-record insertions 
- Reads_WithoutCompression performs 75,000 single-record reads

View source [here](https://github.com/JeringTech/KeyValueStore/blob/main/perf/KeyValueStore/LowMemoryUsageBenchmarks.cs).

Results:

|                     Method |       Mean |    Error |    StdDev |     Median |      Gen 0 |      Gen 1 |     Gen 2 | Allocated |
|--------------------------- |-----------:|---------:|----------:|-----------:|-----------:|-----------:|----------:|----------:|
| Inserts_WithoutCompression |   685.6 ms | 73.33 ms | 201.98 ms |   615.5 ms | 52000.0000 | 17000.0000 | 4000.0000 | 217.97 MB |
|   Reads_WithoutCompression | 1,197.2 ms | 23.69 ms |  26.33 ms | 1,190.0 ms | 38000.0000 | 13000.0000 |         - | 156.28 MB |

``` ini

BenchmarkDotNet=v0.12.1, OS=Windows 10.0.19041.928 (2004/?/20H1)
Intel Core i7-7700 CPU 3.60GHz (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
.NET Core SDK=5.0.300-preview.21180.15
  [Host]     : .NET Core 5.0.6 (CoreCLR 5.0.621.22011, CoreFX 5.0.621.22011), X64 RyuJIT
  Job-JXJRVC : .NET Core 5.0.6 (CoreCLR 5.0.621.22011, CoreFX 5.0.621.22011), X64 RyuJIT

InvocationCount=1  UnrollFactor=1  

```

#### Cursory analysis
Insert performance is excellent. Per-second insertion rate beats disk-based alternatives by an order of magnitude or more.  
Read performance is good - similar to the fastest disk-based alternatives.

### Future Performance Improvements
Performance of the current `MixedStorageKeyValueStore` implementation exceeds our requirements. That said, we're open to pull-requests improving performance.
Several low-hanging fruit:

- Support [Faster's read only cache](https://microsoft.github.io/FASTER/docs/fasterkv-tuning/#configuring-the-read-cache). This is an in-memory 
  cache of recently read records. Depending on read-patterns, this could reduce average read latency significantly.

- Fast-path for blittable types: [Blittable types](https://docs.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types) are fixed-length value-types.
  Instances of these types can be converted to their binary forms without going through MessagePack C#. Also they are fixed-length and so do not need `SpanByte` wrappers. 
  We ran internal benchmarks for a `MixedStorageKeyValueStore<int, string>` store with a fast path for its `int` keys. Read performance improved by ~20%. 

- Use object log for mostly-in-memory situations. 

## Building and Testing
You can build and test this project in Visual Studio 2019.

## Alternatives
- [Faster](https://microsoft.github.io/FASTER)

- [ManagedEsent `PersistentDictionary`](https://github.com/microsoft/ManagedEsent/blob/master/Documentation/PersistentDictionaryDocumentation.md)

- [SQLite](https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/?tabs=netcore-cli)

- [LiteDB](https://www.litedb.org/)

- [MemoryCache](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.caching.memorycache?view=dotnet-plat-ext-5.0)

## Related Concepts
### Faster Basics
This section provides enough information about Faster to use this library effectively. Refer to the 
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
instead they are created at the segment size of our choosing (see [`MixedStorageKVStoreOptions.SegmentSizeBits`](#mixedstoragekvstoreoptions-class)) and populated gradually.
Choosing a larger segment size means more empty, "reserved" disk space. Choosing a smaller segment size means creating more files. It's up to you to
to weigh tradeoffs.

The log is also subdivided into **pages**. Pages are a separate concept from segments - segments typically consist of multiple pages.
Pages are contiguous blocks of in-memory or on-disk storage. What are pages for? Records are moved around as pages. For example, when we add records, 
they are held in memory and only written to the log after we've added enough to fill a page. You can specify page size using [`MixedStorageKVStoreOptions.PageSizeBits`](#mixedstoragekvstoreoptions-class).

Note: Both segments and pages affect Faster in ways that aren't relevant to the current `MixedStorageKeyValueStore` implementation. Refer to the official Faster documentation
to learn more about these concepts.

#### Sessions
Most interactions with a `FasterKV` instance are done through sessions. A session's members are not thread safe:

```csharp
// Create FasterKV instance
FasterKV<int, string> fasterKV = new(1L << 20, logSettings); // logSettings is an instance of LogSettings

// Create session
var session = fasterKV.For(simpleFunctions).NewSession<SimpleFunctions<int, string>>(); // simpleFunction is an instance of SimpleFunctions<int, string>

// Perform operations
await session.UpsertAsync(0, "dummyString").ConfigureAwait(false); // Not thread-safe. You need to manage a pool of sessions for multi-threaded logic.
```

`MixedStorageKeyValueStore` abstracts sessions away:  
```csharp
// Create MixedStorageKeyValueStore instance
MixedStorageKeyValueStore<int, string> mixedStorageKeyValueStore = new();

// Perform operations
await mixedStorageKeyValueStore.UpsertAsync().ConfigureAwait(false); // Thread-safe
```

#### Serialization
Faster differentiates between fixed-length types (primitives, structs with only primitive members etc),
and variable-length types (reference types, structs with variable-length members etc).

By default, `FasterKV` serializes variable-length types using [`DataContractSerializer`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.serialization.datacontractserializer?view=net-5.0),
a slow, space-inefficient XML serializer. It writes serialized variable-length data to a secondary log, the "object log".
Use of the object log slows down reads and writes.

To keep all data in the primary log, the Faster team added the `SpanByte` struct. 
`SpanByte` can be thought of as a wrapper for "integer specifying data length" + "serialized variable-length data".
If a `FasterKV` instance is instantiated with `Spanbyte` in place of variable-length types, for example, `FasterKV<int, SpanByte>`
instead of `FasterKV<int, DummyClass>`, *all data* is written to the primary log. 

To use `SpanByte`, you need to manually serialize variable-length types and instantiate `SpanByte`s around the resultant data.  

`MixedStorageKeyValueStore` abstracts all that away: 
```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, DummyClass>();

// Upsert updates or inserts records.
//
// Under-the-hood, MixedStorageKeyValueStore serializes dummyClassInstance using the MessagePack C# library, 
// creates a `SpanByte` around the resultant data, and passes the `SpanByte` to the underlying FasterKV instance.
mixedStorageKVStore.Upsert(0, dummyClassInstance); 
```

Note: Writing to the object log is more performant than the `SpanByte` system when most of the log is in memory.
Supporting object log for such situations is listed under [Future Performance Improvements](#future-performance-improvements).

## Contributing
Contributions are welcome!

### Contributors
- [JeremyTCD](https://github.com/JeremyTCD)

Thanks to [badrishc](https://github.com/badrishc) for help getting started with Faster. Quite a bit of this library is based
on his suggestions.

## About
Follow [@JeringTech](https://twitter.com/JeringTech) for updates and more.
