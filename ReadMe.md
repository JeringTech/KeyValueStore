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
[Related Concepts](#related-concepts)  
[Contributing](#contributing)  
[About](#about)  

## Overview
Jering.KeyValueStore provides you with a key-value store, `MixedStorageKVStore`, that stores data across memory and disk. You can use this store for application-level caches.

Usage example:

```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, string>();

// Insert
mixedStorageKVStore.Upsert(0, "dummyString1");

// Read asynchronously
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

This library is a third-party wrapper of [Microsoft.Faster](https://github.com/microsoft/FASTER) key-value store. As it's name implies, Faster is a performant key-value store.
See [Faster Basics](#faster-basics) for an introduction to Faster and a description of what this library provides on top of Faster. Note that at present, only a subset 
of Faster's features are supported. In particular, data persistence is not supported.

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
### Concurrency
Faster is designed to be extremely performant in highly concurrent programs. `MixedStorageKVStore.Upsert`, `MixedStorageKVStore.Delete` and `MixedStorageKVStore.ReadAsync` are thread-safe so
you can easily take advantage of this Faster feature:

```csharp
var mixedStorageKVStore = new MixedStorageKVStore<int, string>();
int numRecords = 100_000;

// Concurrent inserts
Parallel.For(0, numRecords, key => mixedStorageKVStore.Upsert(key, "dummyString1"));

// Concurrent reads
List<Task<(Status, string?)>> pendingTasks = new();
for (int i = 0; i < numRecords; i++)
{
    pendingTasks.Add(mixedStorageKVStore.ReadAsync(i).AsTask()); // ReadAsync returns ValueTask, convert to Task so we can use Task.WhenAll
}
await Task.WhenAll(pendingTasks).ConfigureAwait(false);

// Verify reads
for (int i = 0; i < numRecords; i++)
{
    Assert.Equal((Status.OK, "dummyString1"), pendingTasks[i].Result);
}

// Concurrent updates
Parallel.For(0, numRecords, key => mixedStorageKVStore.Upsert(key, "dummyString2"));

// Read again so we can verify updates
pendingTasks.Clear();
for (int i = 0; i < numRecords; i++)
{
    pendingTasks.Add(mixedStorageKVStore.ReadAsync(i).AsTask()); // ReadAsync returns ValueTask, convert to Task so we can use Task.WhenAll
}
await Task.WhenAll(pendingTasks).ConfigureAwait(false);

// Verify updates
for (int i = 0; i < numRecords; i++)
{
    Assert.Equal((Status.OK, "dummyString2"), pendingTasks[i].Result);
}

// Delete using multiple threads
Parallel.For(0, numRecords, key => mixedStorageKVStore.Delete(key));

// Read again so we can verify deletes
pendingTasks.Clear();
for (int i = 0; i < numRecords; i++)
{
    pendingTasks.Add(mixedStorageKVStore.ReadAsync(i).AsTask()); // ReadAsync returns ValueTask, convert to Task so we can use Task.WhenAll
}
await Task.WhenAll(pendingTasks).ConfigureAwait(false);

// Verify deletes
for (int i = 0; i < numRecords; i++)
{
    Assert.Equal((Status.NOTFOUND, null), pendingTasks[i].Result);
}
```

### Key and Value Types
This library uses [MessagePack](https://github.com/neuecc/MessagePack-CSharp) to serialize keys and values to binary that is then [passed to Faster](TODO link to spanbytes)
(MessagePack is a popular, performant binary serializer - Signalr, Microsoft's websockets library, uses MessagePack). 
As such, keys and values can be [any type MessagePack can serialize](https://github.com/neuecc/MessagePack-CSharp#built-in-supported-types). Note that custom types must be 
annotated according to [MessagePack conventions](https://github.com/neuecc/MessagePack-CSharp#object-serialization). 

#### Common Key and Value Types
The next few examples demonstrate common key and value types. 

Given class `DummyClass`:
```csharp
[MessagePackObject]
public class DummyClass
{
    [Key(0)]
    public string? DummyString { get; set; }

    [Key(1)]
    public string[]? DummyStringArray { get; set; }

    [Key(2)]
    public int DummyInt { get; set; }

    [Key(3)]
    public int[]? DummyIntArray { get; set; }
}
```
`object` key (`string`), `object` value (`DummyClass`) `MixedStorageKVStore`:
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
Primitive key (`int`), value-type value (`DummyStruct`) `MixedStorageKVStore`:
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

### Configuration
TODO notes on Configuration

### Managing Data on Disk
When is data written to disk?

Where is on-disk data located? By default, `<temp path>/FasterLogs`. Set delete on close to false 
to view files:

```csharp
TODO
```

TODO monitor disk capacity

### Value Compression
MessagePack is a serializer, not a compressor. However, the MessagePack library we use provides compression out-of-the-box.
By default, `MixedStorageKVStore` is configured to use [Lz4BlockArray](https://github.com/neuecc/MessagePack-CSharp#lz4-compression) compression. 
This reduces the size of your data in the store at the cost of CPU cycles.  

To disable compression:

```
var mixedStorageKVStoreOptions = new MixedStorageKVStoreOptions()
{
    MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
};
var mixedStorageKVStore = new MixedStorageKVStore<DummyClass, string>(mixedStorageKVStoreOptions);
```

## API
<!-- GENERATED_API_DOCS_START -->

<!-- GENERATED_API_DOCS_END --> 

#### Mutable Object Type as Key Type
Under-the-hood, the binary serialized form of what you pass as keys are the keys passed to Faster.
This means caution is required if you specify a mutable object type as your key type, for example `DummyClass` from above.
Consider the situation where you insert a value using an instance as key, and then you change a member of the instance. 
When you try to read the value, you will either not read any value or read a different value:

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

## Performance
TODO

## Building and Testing
You can build and test this project in Visual Studio 2019.

## Alternatives
<!-- TODO pros/cons -->

- [ManagedEsent `PersistentDictionary`](https://github.com/microsoft/ManagedEsent/blob/master/Documentation/PersistentDictionaryDocumentation.md)

- [SQLite](https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/?tabs=netcore-cli)

- [LiteDB](https://www.litedb.org/)

- [MemoryCache](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.caching.memorycache?view=dotnet-plat-ext-5.0)

## Related Concepts
### Faster Basics
TODO

## Contributing
Contributions are welcome!

### Contributors
- [JeremyTCD](https://github.com/JeremyTCD)
- [badrishc](https://github.com/badrishc) (through comments in Microsoft.Faster issues)

## About
Follow [@JeringTech](https://twitter.com/JeringTech) for updates and more.
