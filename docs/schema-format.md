# Schema format

The input is a JSON Schema bundle with a `definitions` object. `ClientRequest` defines outgoing requests; optional `ClientNotification`, `ServerNotification`, and `ServerRequest` definitions describe the other message directions. Message envelopes specify a method through a single string `enum` or `const`, with parameter schemas under `params`. Requests must require an `id`.

For example, a small echo protocol:

```json
{
  "definitions": {
    "ClientRequest": {
      "oneOf": [{
        "type": "object",
        "required": ["id", "method", "params"],
        "properties": {
          "id": { "type": "string" },
          "method": { "const": "echo" },
          "params": { "$ref": "#/definitions/EchoParams" }
        }
      }]
    },
    "EchoParams": {
      "type": "object",
      "required": ["message"],
      "properties": { "message": { "type": "string" } }
    },
    "EchoResponse": {
      "type": "object",
      "required": ["message"],
      "properties": { "message": { "type": "string" } }
    }
  }
}
```

Response types are resolved by convention: `EchoParams` maps to `EchoResponse`. Without a parameter reference, the normalized method name plus `Response` is used. Set `ResponseSchemaNames` to override individual associations. Missing response definitions fail generation, and inferred associations appear in `Diagnostics`.

`SupplementalSchemaJson` supplies missing definitions; the primary bundle wins when names overlap. Directory generation also accepts individual schemas named by their `title` or filename. References use local `#/definitions/...` paths. OpenRPC input is not supported.

## Message directions

| Definition | Generated API |
| --- | --- |
| `ClientRequest` | Methods returning `ValueTask<TResponse>`. Required. |
| `ClientNotification` | Methods returning `ValueTask`. |
| `ServerNotification` | Typed `...Received` events. |
| `ServerRequest` | Assignable `...Handler` callbacks returning `ValueTask<TResponse>`. |

Missing optional message groups are reported in diagnostics. Each method must have a unique wire name within its group.

## Response mappings

Set an explicit mapping when the response name does not follow the parameter name:

```csharp
ResponseSchemaNames = new Dictionary<string, string>
{
    ["echo"] = "EchoResponse"
}
```

## Combining schemas

`GenerateDirectory` recursively reads JSON files, excluding the output directory, `bin`, `obj`, `.git`, and directory links. The bundle containing `ClientRequest` with the most flat definitions is primary; other inputs supply missing definitions. Ties use ordinal file-path order. Use schemas from the same protocol version.

## Supported model shapes

Objects, recursive references, arrays, dictionaries, object `allOf`, string enums, primitive types, and `oneOf`/`anyOf` unions are supported. Union wrappers expose `Variant`, `FromVariant1(...)`, and `AsVariant1()` methods. Nullable two-branch unions become nullable C# types.

Required properties use C# `required`. Optional properties use `Optional<T>`: `default` omits the field, while an explicitly assigned null is written as JSON null. Additional properties are preserved as extension data.

Schemas explicitly allowing arbitrary JSON (`true` or `{}`) use `JsonElement`. Unsupported shapes fail by default; `FailOnUntypedSchemas = false` permits fallbacks reported in `Diagnostics`.

Union readers check supported shapes, required properties, enum/const tags, compositions, additional properties, and numeric bounds. This is not a complete JSON Schema validator. String formats remain strings, and CLR numeric ranges apply.

[Back to the README](../README.md)
