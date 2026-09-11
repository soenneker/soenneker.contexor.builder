[![NuGet version](https://img.shields.io/nuget/v/soenneker.contexor.builder.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.contexor.builder/)
[![Publish workflow](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.contexor.builder/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.contexor.builder/actions/workflows/publish-package.yml)
[![NuGet downloads](https://img.shields.io/nuget/dt/soenneker.contexor.builder.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.contexor.builder/)

# Soenneker.Contexor.Builder

Generate a strongly typed C# client for your JSON-RPC service from its JSON Schema protocol bundle. Call methods, receive notifications, and handle server requests through a consistent API instead of writing message envelopes and serialization code by hand.

The builder produces a .NET 10 library project with source files and package references, ready to build or reference from your application.

## Features

| Feature | Support |
| --- | --- |
| Typed clients | Asynchronous request methods, notification methods, and a client interface. |
| Bidirectional messaging | Typed notification events and server-request handlers. |
| Transports | Newline-delimited JSON over streams/stdio and JSON messages over WebSockets. Custom transports implement `IJsonRpcTransport`. |
| Models | Objects, recursive references, arrays, dictionaries, string enums, and typed unions. |
| JSON fidelity | Original property names and enum values, required members, omitted versus null values, and extension data. |
| Serialization | System.Text.Json source-generated metadata, cached type information, and compiled union matching. |
| Request handling | Concurrent calls, response correlation, cancellation, and typed RPC errors. |
| Project output | A `.csproj` with Soenneker dependencies, one type per file, and interfaces in `Abstract`. |

Generation and generated clients require .NET 10. Input is a [JSON Schema protocol bundle](docs/schema-format.md); OpenAPI and OpenRPC documents are not supported. Connection setup and authentication belong to the application. HTTP POST transport, batching, and automatic retries are not generated.

## Getting started

### 1. Install and register the builder

```sh
dotnet add package Soenneker.Contexor.Builder
```

In your application's service registration:

```csharp
using Soenneker.Contexor.Builder.Registrars;

services.AddContexorBuilderAsSingleton();
```

Inject `IContexorBuilder` from `Soenneker.Contexor.Builder.Abstract` into the component that generates your client.

### 2. Generate your client

Save the [example echo schema](docs/schema-format.md) as `schemas/protocol.json`, or supply your own protocol bundle. With the injected `builder`:

```csharp
using Soenneker.Contexor.Builder;

var result = await builder.GenerateFile(
    schemaPath: "schemas/protocol.json",
    outputDirectory: "Generated/ServiceClient",
    options: new ContexorBuilderOptions
    {
        Namespace = "MyApp.Service",
        ClientName = "ServiceClient"
    },
    cancellationToken: cancellationToken);

foreach (string diagnostic in result.Diagnostics)
    Console.WriteLine(diagnostic);
```

The output includes:

```text
Generated/ServiceClient/
  ServiceClient.csproj
  ServiceClient.cs
  Abstract/
    IServiceClient.cs
    IJsonRpcTransport.cs
  Models/
    EchoParams.cs
    EchoResponse.cs
  StreamJsonRpcTransport.cs
  WebSocketJsonRpcTransport.cs
  ...serialization and transport support types
```

### 3. Reference the generated project

```sh
dotnet build Generated/ServiceClient/ServiceClient.csproj
dotnet add MyApp/MyApp.csproj reference Generated/ServiceClient/ServiceClient.csproj
```

Package references are included automatically. The generated client does not need the builder package at runtime.

### 4. Call your service

For a service implementing the echo schema, pass its connected read and write streams:

```csharp
using MyApp.Service;
using MyApp.Service.Models;

await using var transport = new StreamJsonRpcTransport(inputStream, outputStream);
await using var client = new ServiceClient(transport);

EchoResponse response = await client.Echo(
    new EchoParams { Message = "Hello" }, cancellationToken);
Console.WriteLine(response.Message);
```

For a WebSocket connection, use `new WebSocketJsonRpcTransport(connectedSocket)`. Transports leave supplied streams and sockets open by default. Dispose the client before its transport, then close the connection. The application owns any server process it starts.

Generated asynchronous methods use names such as `Echo`, without an appended `Async`. `DisposeAsync` retains its .NET interface name.

## Generation options

| Option | Default | Purpose |
| --- | --- | --- |
| `Namespace` | `Generated.Client` | Root namespace for generated types. |
| `ClientName` | `RpcClient` | Client class, source filename, and project name. |
| `ResponseSchemaNames` | Empty | Explicit method-to-response schema mappings. |
| `SupplementalSchemaJson` | Empty | Additional bundles supplying missing definitions. |
| `FailOnUntypedSchemas` | `true` | Reject unsupported shapes instead of emitting diagnosed fallbacks. |
| `OmitJsonRpcVersion` | `false` | Omit `jsonrpc` for peers that require its absence. |
| `Overwrite` | `false` | Replace existing files with matching generated names. |

Use `Generate(schemaJson, options)` to inspect output in memory. Use `GenerateFile` for a bundle on disk or `GenerateDirectory` to combine bundled and individual schemas from a directory. All return `Files`, `ClientType`, `Diagnostics`, and `RequiredPackages`.

Generation preserves unrelated files. When regenerating, set `Overwrite = true` and remove obsolete generated files after contract changes. Individual file replacements are atomic; the directory is not replaced as one transaction.

## Notifications, handlers, and errors

Subscribe to generated `...Received` events before making calls that produce notifications. `UnknownNotification` receives notifications absent from the schema. Assign `...Handler` properties for server requests; handlers receive typed parameters and a cancellation token and return a `ValueTask<TResponse>`. Unregistered requests receive error `-32601`.

Handlers and events run serially. A handler may await an outgoing request, but must not wait for a later notification. Observe `client.Completion` for dispatch failures. Remote errors throw `JsonRpcException`; malformed envelopes throw `JsonRpcProtocolException`. Connection failures fail pending calls. Cancellation stops local waiting without undoing work already accepted by the server.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Response schema cannot be resolved | Supply the missing definition or set `ResponseSchemaNames`. By convention, `EchoParams` maps to `EchoResponse`. |
| An output file already exists | Enable `Overwrite` when intentionally regenerating. |
| A schema shape is unsupported | Review diagnostics and the [supported shapes](docs/schema-format.md#supported-model-shapes). |
| Connection or envelope errors | Check transport framing, the peer's version-member requirements, and `client.Completion`. |

## Examples and reference

- [Schema format and message directions](docs/schema-format.md)
- [Runnable app-server integration](examples/AppServerClient/README.md)
- [Builder interface](src/Soenneker.Contexor.Builder/Abstract/IContexorBuilder.cs)
- [Generation options](src/Soenneker.Contexor.Builder/ContexorBuilderOptions.cs)
