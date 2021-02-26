# WIP

A key-value store. Thin wrapper of [Microsoft.Faster](https://github.com/microsoft/FASTER) that abstracts away some of
Faster's complexities, namely:

- Sessions
- Binary serialization,
- Use of `SpanByte`
- Log compaction
- Disposal
- Configuration for general use

Initial key value store is functional. Package isn't published yet. This readme is a template at the moment.

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

## API

## Performance

## Building and Testing
You can build and test this project in Visual Studio 2019.

## Alternatives
MemoryCache

SQLite

LiteDB

https://github.com/microsoft/ManagedEsent

## Related Concepts

## Contributing
Contributions are welcome!

### Contributors
- [JeremyTCD](https://github.com/JeremyTCD)
- [badrishc](https://github.com/badrishc) ([Microsoft.Faster issue #403](https://github.com/microsoft/FASTER/issues/403))

## About
Follow [@JeringTech](https://twitter.com/JeringTech) for updates and more.
