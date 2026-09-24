using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Soenneker.JsonSchema.ToCSharp;

namespace Soenneker.Contexor.Builder.Internal;

internal sealed class SchemaEmitter
{
    private readonly ContexorBuilderOptions _options;
    private readonly JsonSchemaToCSharpResult _result;
    private readonly Dictionary<JsonNode, string> _pointers = new();

    internal SchemaEmitter(JsonObject document, ContexorBuilderOptions options, CancellationToken ct)
    {
        CSharpNames.Validate(options.Namespace, nameof(options.Namespace), true);
        CSharpNames.Validate(options.ClientName, nameof(options.ClientName));
        if (options.ClientName is "OptionalConverter" or "JsonTypeCache" or "Optional" or "OptionalConverterFactory" or "Models" or "SchemaJsonContext" ||
            options.ClientName.StartsWith("IOptional", StringComparison.Ordinal))
            throw new ArgumentException("The client name conflicts with generated infrastructure.", nameof(options.ClientName));
        _options = options;
        var schema = (JsonObject)document.DeepClone();
        JsonObject definitions = schema["definitions"]!.AsObject();
        foreach ((string name, JsonNode? node) in document["definitions"]!.AsObject())
            if (node != null) _pointers[node] = Pointer(name);

        // Give inline protocol parameters a public type mapping, and make envelopes
        // reference that same model so optional converter registrations also agree.
        var names = definitions.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (string group in new[] { "ClientRequest", "ClientNotification", "ServerNotification", "ServerRequest" })
        {
            if (document["definitions"]![group] is not JsonObject original) continue;
            JsonObject copy = definitions[group]!.AsObject();
            JsonObject[] originals = original["oneOf"] is JsonArray branches ? branches.Select(b => b!.AsObject()).ToArray() : [original];
            JsonObject[] copies = copy["oneOf"] is JsonArray cloned ? cloned.Select(b => b!.AsObject()).ToArray() : [copy];
            for (int i = 0; i < originals.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                JsonNode? parameter = originals[i]["properties"]?["params"];
                if (parameter == null || parameter["type"]?.ToString() == "null") continue;
                JsonNode? method = originals[i]["properties"]?["method"];
                string wire = method?["const"]?.GetValue<string>() ?? method?["enum"]?[0]?.GetValue<string>() ?? group;
                string name = CSharpNames.Unique(CSharpNames.Identifier(wire) + "Params", names);
                definitions[name] = parameter.DeepClone();
                string pointer = Pointer(name);
                _pointers[parameter] = pointer;
                copies[i]["properties"]!["params"] = new JsonObject { ["$ref"] = pointer };
            }
        }
        schema["$ref"] = Pointer("ClientRequest");
        _result = new JsonSchemaToCSharp().Generate(schema.ToJsonString(), new JsonSchemaToCSharpOptions
        {
            Namespace = options.Namespace,
            FailOnUntypedSchemas = options.FailOnUntypedSchemas,
            InferTypesFromKeywords = false
        }, ct);
    }

    internal string GetType(JsonNode schema, string name)
    {
        string pointer = _pointers.TryGetValue(schema, out string? mapped) ? mapped
            : schema["$ref"]?.GetValue<string>() ?? throw new ArgumentException("Missing protocol schema mapping: " + name);
        return _result.NamedTypes[pointer];
    }

    internal ContexorBuildResult Finish()
    {
        var files = new Dictionary<string, string>(_result.Files, StringComparer.Ordinal);
        // Preserve the existing public serialization context name.
        foreach (string name in new[] { "SchemaJsonContext.cs", "JsonTypes.cs", "JsonTypeCache.cs" })
        {
            string source = files[name].Replace("class SchemaJsonContext :", "class RpcJsonContext :", StringComparison.Ordinal)
                .Replace("SchemaJsonContext.Default", "RpcJsonContext.Default", StringComparison.Ordinal);
            files.Remove(name);
            files[name == "SchemaJsonContext.cs" ? "RpcJsonContext.cs" : name] = source;
        }
        files["PooledJsonBuffer.cs"] = Template("PooledJsonBuffer").Replace("@@NAMESPACE@@", _options.Namespace);
        return new ContexorBuildResult(files, "global::" + _options.Namespace + "." + _options.ClientName, _result.Diagnostics);
    }

    private static string Pointer(string name) => "#/definitions/" + name.Replace("~", "~0").Replace("/", "~1");

    internal static string Template(string name)
    {
        using Stream stream = typeof(SchemaEmitter).Assembly.GetManifestResourceStream("Contexor.Templates." + name + ".txt")
            ?? throw new InvalidOperationException("Missing generation template: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
