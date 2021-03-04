# WIP

A key-value store. Thin wrapper of [Microsoft.Faster](https://github.com/microsoft/FASTER) that abstracts away some of
Faster's complexities, namely:

- Sessions
- Support for variable length types using `SpanByte` and MessagePack for binary serialization
- Periodic log compaction
- Configuration for general use

`MixedStorageKVStore` is functional but the package Jering.KeyValueStore hasn't been published. This readme is incomplete.

# Jering.KeyValueStore
[![Build Status](https://dev.azure.com/JeringTech/KeyValueStore/_apis/build/status/Jering.KeyValueStore-CI?branchName=master)](https://dev.azure.com/JeringTech/KeyValueStore/_build/latest?definitionId=1?branchName=master)
[![codecov](https://codecov.io/gh/JeringTech/KeyValueStore/branch/master/graph/badge.svg)](https://codecov.io/gh/JeringTech/KeyValueStore)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/JeringTech/KeyValueStore/blob/master/License.md)
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
[Relationship with Faster](#relationship-with-faster)  
[Alternatives](#alternatives)  
[Related Concepts](#related-concepts)  
[Contributing](#contributing)  
[About](#about)  

## Overview
Jering.KeyValueStore enables you to store key-value data across memory and disk. Use this library for non-persistent, application-level caches.

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

This library is a third-party wrapper of [Microsoft's Faster key-value store](https://github.com/microsoft/FASTER) (Faster). Faster introduces a novel, lock-free system 
that is performant in highly concurrent situations. This document requires a basic understanding of Faster. Refer to [Faster Basics](#faster-basics) 
for a quick primer. 

The main class this library exposes, `MixedStorageKVStore`, isn't as optimized as a custom `FasterKV` instance could be. Also, it only exposes a subset of Faster's features.
In particular, persistence is not exposed (though you can handle it [manually](#advanced-configuration)). That said, `MixedStorageKVStore` performs well relative to [alternatives](#alternatives) 
and is trivial to use. Refer to [Relationship with Faster](#relationship-with-faster) for details on what this library provides on top of faster as well as drawbacks to using 
this library vs Faster directly.

## Target Frameworks
- .NET Standard 2.1

## Platforms
Works on Windows, macOS, and Linux systems.
 
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
binary serialization library - Signalr, Microsoft's websockets library uses it). Note that custom types must be annotated according 
to [MessagePack C# conventions](https://github.com/neuecc/MessagePack-CSharp#object-serialization). 

#### Common Key and Value Types
The following examples demonstrate common key and value types. 

Given class `DummyClass`:
```csharp
[MessagePackObject]
public class DummyClass
{
    [Key(0)] // Similar to JsonPropertyNameAttribute for System.Text.Json
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

The following table lists all available options.

#### MixedStorageKVStoreOptions
| Option | Type | Description | Default |  
| ------ | ---- | ----------- | ------- |
| IndexNumBuckets | `long` | The number of buckets in Faster's main index. Each bucket is 64 bits. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | 1048576 (64 MB index) |
| PageSizeBits | `int` | The size of a page in Faster's log. A page is a contiguous block of in-memory or on-disk storage. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | 25 (2^25 = 33.5 MB) |
| MemorySizeBits | `int` | The size of the in-memory region of Faster's log. If the log outgrows this region, overflow is moved to its on-disk region. Memory size must be at least two pages large. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | 26 (2^26 = 67 MB) |
| SegmentSizeBits | `int` | The size of a segment of the on-disk region of Faster's log. What is a segment? The on-disk region of the log is stored across multiple files, each file is referred to as a segment. For performance reasons, segments are "pre-allocated". This means they are not created empty and left to  grow gradually, instead they are created at the size specified by this value and populated gradually. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | 28 (268 MB)  |
| LogDirectory | `string` | The directory containing the on-disk region of Faster's log. If this value is `null`, whitespace or an empty string, log files are placed in "&lt;temporary path&gt;/FasterLogs" where "&lt;temporary path&gt;" is the value returned by `Path.GetTempPath`. Note that nothing is written to disk while your log fits in-memory. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | `null`  |
| LogFileNamePrefix | `string` | The Faster-log filename prefix. The on-disk region of the log is stored across multiple files. Each file is referred to as a segment. Each segment has file name "&lt;log file name prefix&gt;.log.&lt;segment number&gt;". If this value is `null`, whitespace or an empty string, a random `Guid` is used as the prefix. This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | `null`  |
| TimeBetweenLogCompactionsMS | `int` | The time between Faster-log compaction attempts. If this value is negative, log compaction is disabled. | `60000`  |
| InitialLogCompactionThresholdBytes | `long` | The initial log compaction threshold. Initially, log compactions only run when the Faster-log's safe-readonly region's size is larger than or equal to this value. If log compactions run 5 times in a row, this value is doubled. Why? Consider the situation where the safe-readonly region is already compact, but still larger than the threshold. Not increasing the threshold would result in continual redundant compaction runs. If this value is less than or equal to 0, the initial log compaction threshold is 2 * memory size (`MemorySizeBits`). | `0`  |
| DeleteLogOnClose | `bool` | The value specifying whether Faster-log files are deleted when the `MixedStorageKVStore` is disposed or finalized (at which points underlying Faster-log files are closed). This value is ignored if a `FasterKV` instance is supplied to the `MixedStorageKVStore` constructor. | `true` |

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

With a reference to the`FasterKV<SpanByte, SpanByte>` instance, you can manually handle Faster features like persistence (checkpoints and recovery).

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
While `MixedStorageKVStore` performs [log compaction](log-compaction) periodically, data can only be so compact. So long as you're adding new key-value pairs, 
the size of your data can grow boundlessly. Therefore, we recommend monitoring disk space the same way you would monitor memory or CPU usage. For example, if you're 
using an Azure VM, consider setting an alert for when disk space usage reaches a certain percentage.

## API
TODO fasterkv property

### IMixedStorageKVStore<TKey, TValue>.Upsert
#### Signature
```
void Upsert(TKey key, TValue obj);
```
#### Description
Updates or inserts a record.
#### Parameters
- `key`
  - Type: `TKey`
  - Description: The key of the record.
- `obj`
  - Type: `TValue`
  - Description: The new value of the record.
#### Exceptions
- `ObjectDisposedException`
  - Thrown if the instance or a dependency is disposed.

### IMixedStorageKVStore<TKey, TValue>.Delete
#### Signature
```
Status Delete(TKey key);
```
#### Description
Deletes a record.
#### Parameters
- `key`
  - Type: `TKey`
  - Description: The key of the record to delete.
#### Exceptions
- `ObjectDisposedException`
  - Thrown if the instance or a dependency is disposed.

### IMixedStorageKVStore<TKey, TValue>.ReadAsync
#### Signature
```
ValueTask<(Status, TValue?)> ReadAsync(TKey key);
```
#### Description
Reads a record asynchronously.
#### Parameters
- `key`
  - Type: `TKey`
  - Description: The key of the record to read.
#### Returns
The task representing the asynchronous operation.
#### Exceptions
- `ObjectDisposedException`
  - Thrown if the instance or a dependency is disposed.

#### Serialization Options
Configure MessagePack C# by specifying `MixedStorageKVStoreOptions.MessagePackSerializerOptions`. Of particular note is compression.
"MessagePack" the format, much like JSON the format, does not compress data. However, "MessagePack C#" the library provides compression out-of-the-box.
By default, `MixedStorageKVStoreOptions.MessagePackSerializerOptions` is configured so we use [Lz4BlockArray](https://github.com/neuecc/MessagePack-CSharp#lz4-compression) compression. 
This reduces the size of your data in the store at the cost of CPU cycles. Depending on your needs you might decide to disable compression. To do so:

```csharp
var mixedStorageKVStoreOptions = new MixedStorageKVStoreOptions()
{
    MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
};
var mixedStorageKVStore = new MixedStorageKVStore<int, string>(mixedStorageKVStoreOptions);
```

## Performance
### Benchmarks
TODO notes
- class used
- unlike alternatives doesn't block on write

TODO
- upserts
- reads

### Future improvements
TODO Low hanging fruit
- don't serialize blittable types
- read only cache

## Building and Testing
You can build and test this project in Visual Studio 2019.

## Relationship with Faster
### Features on Top of Faster
TODO
- Sessions
- Support for variable length types using `SpanByte` and MessagePack for binary serialization
- Periodic log compaction
### When You Might Want to Use Faster Directly
TODO apart from implementing the above features in different ways
- custom functions
- rmw
- log operations (scanning, truncation etc)

## Alternatives
<!-- TODO pros/cons -->

- [ManagedEsent `PersistentDictionary`](https://github.com/microsoft/ManagedEsent/blob/master/Documentation/PersistentDictionaryDocumentation.md)

- [SQLite](https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/?tabs=netcore-cli)

- [LiteDB](https://www.litedb.org/)

- [MemoryCache](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.caching.memorycache?view=dotnet-plat-ext-5.0)

## Related Concepts
### Faster Basics
TODO overview
- log
- log compaction
- object log and spanbyte
- sessions and concurrency

## Contributing
Contributions are welcome!

### Contributors
- [JeremyTCD](https://github.com/JeremyTCD)

## About
Follow [@JeringTech](https://twitter.com/JeringTech) for updates and more.
