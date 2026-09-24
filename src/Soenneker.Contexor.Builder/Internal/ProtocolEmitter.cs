using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Soenneker.Utils.PooledStringBuilders;

namespace Soenneker.Contexor.Builder.Internal;

internal sealed class ProtocolEmitter
{
    private readonly JsonObject _document;
    private readonly ContexorBuilderOptions _options;
    private readonly CancellationToken _ct;
    private readonly List<string> _diagnostics = [];

    internal ProtocolEmitter(JsonObject document, ContexorBuilderOptions options, CancellationToken ct)
    {
        _document = document;
        _options = options;
        _ct = ct;
        if (document["definitions"] is not JsonObject definitions || definitions["ClientRequest"] is not JsonObject)
            throw new ArgumentException("Expected a JSON Schema protocol bundle containing definitions.ClientRequest.");
        foreach (string supplemental in options.SupplementalSchemaJson)
        {
            JsonObject extra = JsonNode.Parse(supplemental)?["definitions"] as JsonObject
                               ?? throw new ArgumentException("Supplementary schemas must contain definitions.");
            ImportDefinitions(extra, definitions);
        }
        if (new[] { "IJsonRpcTransport", "JsonRpcTransport", "StreamJsonRpcTransport", "WebSocketJsonRpcTransport", "JsonRpcException", "JsonRpcProtocolException", "UnionMatchers", "RpcJsonContext", "JsonTypes", "PooledJsonBuffer" }.Contains(options.ClientName))
            throw new ArgumentException("ClientName conflicts with generated infrastructure.");
    }

    internal static void ImportDefinitions(JsonObject source, JsonObject destination)
    {
        var imports = new List<(string Name, string Path, JsonNode Schema)>();
        Collect(source, "#/definitions/", imports);
        Dictionary<string, string> references = imports.ToDictionary(p => p.Path, p => "#/definitions/" + Escape(p.Name), StringComparer.Ordinal);
        foreach ((string Name, string Path, JsonNode Schema) import in imports)
        {
            if (destination.ContainsKey(import.Name)) continue;
            JsonNode clone = import.Schema.DeepClone();
            RewriteReferences(clone, references);
            destination[import.Name] = clone;
        }
    }

    private static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");
    private static void Collect(JsonObject group, string path, List<(string Name, string Path, JsonNode Schema)> imports)
    {
        foreach ((string name, JsonNode? node) in group)
        {
            if (node is JsonObject obj && obj.Count > 0 && !obj.Any(p => p.Key.StartsWith('$') || p.Key is "type" or "oneOf" or "anyOf" or "allOf" or "enum" or "properties" or "title" or "description"))
                Collect(obj, path + Escape(name) + "/", imports);
            else if (node != null) imports.Add((name, path + Escape(name), node));
        }
    }
    private static void RewriteReferences(JsonNode node, Dictionary<string, string> references)
    {
        if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue value && references.TryGetValue(value.GetValue<string>(), out string? replacement)) obj["$ref"] = replacement;
            foreach (JsonNode? child in obj.Select(p => p.Value)) if (child != null) RewriteReferences(child, references);
        }
        else if (node is JsonArray array) foreach (JsonNode? child in array) if (child != null) RewriteReferences(child, references);
    }

    internal ContexorBuildResult Generate()
    {
        var models = new SchemaEmitter(_document, _options, _ct);
        using var signatures = new PooledStringBuilder(8192);
        using var methods = new PooledStringBuilder(16384);
        using var notifications = new PooledStringBuilder(8192);
        using var requests = new PooledStringBuilder(8192);
        var names = new HashSet<string>(StringComparer.Ordinal)
        { _options.ClientName, "DisposeAsync", "Completion", "UnknownNotification", "Dispatch", "Invoke", "Notify", "ReadResponse", "CreateRequest", "SendNotification" };
        foreach (string group in new[] { "ClientRequest", "ClientNotification", "ServerNotification", "ServerRequest" })
        {
            var wireNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonObject envelope in Envelopes(group).OrderBy(e => WireName(e), StringComparer.Ordinal))
            {
                _ct.ThrowIfCancellationRequested();
                string wire = WireName(envelope);
                if (!wireNames.Add(wire)) throw new ArgumentException("Duplicate method in " + group + ": " + wire);
                bool request = group.EndsWith("Request", StringComparison.Ordinal);
                JsonObject properties = envelope["properties"]!.AsObject();
                HashSet<string> required = (envelope["required"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToHashSet() ?? [];
                if (request && !required.Contains("id")) throw new ArgumentException("Request must require id: " + wire);
                JsonNode? parameter = properties["params"];
                bool hasParams = parameter != null && parameter["type"]?.ToString() != "null";
                string parameterType = hasParams ? models.GetType(parameter!, CSharpNames.Identifier(wire) + "Params") : "global::System.Text.Json.JsonElement";
                string? responseType = request ? models.GetType(ResponseSchema(wire, parameter), CSharpNames.Identifier(wire) + "Response") : null;
                string name = CSharpNames.Unique(CSharpNames.Identifier(wire) + (group.StartsWith("Client", StringComparison.Ordinal) ? "" : request ? "Handler" : "Received"), names);
                string description = CSharpNames.Xml(envelope["description"]?.ToString() ?? wire);
                signatures.AppendLine("    /// <summary>" + description + "</summary>");
                if (group.StartsWith("Client", StringComparison.Ordinal))
                {
                    bool optional = hasParams && !required.Contains("params");

                    string argumentType = optional ? "Optional<" + parameterType + ">" : parameterType;
                    string argument = hasParams ? argumentType + " parameters" + (optional ? " = default" : "") + ", " : "";
                    string returnType = request ? "global::System.Threading.Tasks.ValueTask<" + responseType + ">" : "global::System.Threading.Tasks.ValueTask";
                    string signature = returnType + " " + name + "(" + argument + "global::System.Threading.CancellationToken cancellationToken = default)";
                    signatures.AppendLine("    " + signature + ";");
                    string payload = hasParams ? (optional ? "parameters.IsDefined ? parameters.Value : default!" : "parameters") : "NullElement";
                    string includeParameters = hasParams ? (optional ? "parameters.IsDefined" : "true") : required.Contains("params") ? "true" : "false";
                    methods.AppendLine("    public " + signature + "\n    {\n        cancellationToken.ThrowIfCancellationRequested();");
                    if (hasParams && !optional && !parameterType.EndsWith('?')) methods.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(parameters);");
                    methods.AppendLine("        return " + (request ? "Invoke<" + responseType + ", " + parameterType + ">" : "Notify<" + parameterType + ">") + "(" + CSharpNames.Literal(wire) + ", " + payload + ", " + includeParameters + ", cancellationToken);\n    }");
                }
                else if (request)
                {
                    string handler = "global::System.Func<" + parameterType + ", global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<" + responseType + ">>? " + name + " { get; set; }";
                    signatures.AppendLine("    " + handler);
                    methods.AppendLine("    public " + handler);
                    requests.AppendLine("                case " + CSharpNames.Literal(wire) + ":\n                {\n                    var handler = " + name + ";\n                    if (handler is null) throw new JsonRpcException(-32601, \"No handler registered for \" + method);\n                    var result = await handler(global::System.Text.Json.JsonSerializer.Deserialize(parameters, JsonTypes.Get<" + parameterType + ">(_options))!, cancellationToken).NoSync();\n                    return global::System.Text.Json.JsonSerializer.SerializeToElement(result, JsonTypes.Get<" + responseType + ">(_options));\n                }");
                }
                else
                {
                    string eventSignature = "event global::System.Action<" + parameterType + ">? " + name + ";";
                    signatures.AppendLine("    " + eventSignature);
                    methods.AppendLine("    public " + eventSignature);
                    notifications.AppendLine("                case " + CSharpNames.Literal(wire) + ":\n                    " + name + "?.Invoke(global::System.Text.Json.JsonSerializer.Deserialize(parameters, JsonTypes.Get<" + parameterType + ">(_options))!);\n                    break;");
                }
            }
        }
        // Emit every named model, including definitions not reachable from method signatures.
        foreach ((string name, JsonNode? schema) in _document["definitions"]!.AsObject().OrderBy(p => p.Key, StringComparer.Ordinal))
            if (schema != null && name is not ("ClientRequest" or "ClientNotification" or "ServerRequest" or "ServerNotification")) models.GetType(schema, name);
        ContexorBuildResult result = models.Finish();
        var files = new Dictionary<string, string>(result.Files, StringComparer.Ordinal)
        {
            [_options.ClientName + ".cs"] = SchemaEmitter.Template("ProtocolClient").Replace("@@NAMESPACE@@", _options.Namespace).Replace("@@CLIENT@@", _options.ClientName)
                .Replace("@@SIGNATURES@@", signatures.ToString()).Replace("@@METHODS@@", methods.ToString())
                .Replace("@@NOTIFICATIONS@@", notifications.ToString()).Replace("@@REQUESTS@@", requests.ToString())
                .Replace("@@VERSION@@", _options.OmitJsonRpcVersion ? "false" : "true"),
            ["JsonRpcTransport.cs"] = SchemaEmitter.Template("JsonRpcTransport").Replace("@@NAMESPACE@@", _options.Namespace)
        };
        files["Abstract/I" + _options.ClientName + ".cs"] = SchemaEmitter.Template("IProtocolClient")
            .Replace("@@NAMESPACE@@", _options.Namespace).Replace("@@CLIENT@@", _options.ClientName)
            .Replace("@@SIGNATURES@@", signatures.ToString());
        foreach (string type in new[] { "IJsonRpcTransport", "StreamJsonRpcTransport", "WebSocketJsonRpcTransport", "JsonRpcException", "JsonRpcProtocolException" })
            files[(type == "IJsonRpcTransport" ? "Abstract/" : "") + type + ".cs"] = SchemaEmitter.Template(type).Replace("@@NAMESPACE@@", _options.Namespace);
        using var packages = new PooledStringBuilder(1024);
        foreach ((string package, string version) in result.RequiredPackages.OrderBy(p => p.Key, StringComparer.Ordinal))
            packages.AppendLine("    <PackageReference Include=\"" + package + "\" Version=\"" + version + "\" />");
        files[_options.ClientName + ".csproj"] = SchemaEmitter.Template("Project")
            .Replace("@@NAMESPACE@@", _options.Namespace).Replace("@@CLIENT@@", _options.ClientName)
            .Replace("@@PACKAGES@@", packages.ToString().TrimEnd());
        return result with { Files = new ReadOnlyDictionary<string, string>(files), Diagnostics = result.Diagnostics.Concat(_diagnostics).ToArray() };
    }

    private IEnumerable<JsonObject> Envelopes(string group)
    {
        if (_document["definitions"]![group] is not JsonObject schema)
        {
            if (group != "ClientRequest") _diagnostics.Add(group + " is absent; its typed messages cannot be generated. Supply supplementary definitions if required.");
            return [];
        }
        return schema["oneOf"] is JsonArray branches ? branches.Select(b => b!.AsObject()) : [schema];
    }
    private static string WireName(JsonObject envelope)
    {
        JsonNode? method = envelope["properties"]?["method"];
        string? name = method?["const"]?.GetValue<string>() ?? (method?["enum"] is JsonArray { Count: 1 } values ? values[0]?.GetValue<string>() : null);
        return !string.IsNullOrWhiteSpace(name) ? name : throw new ArgumentException("Envelope method must be a single string enum or const.");
    }
    private JsonNode ResponseSchema(string method, JsonNode? parameter)
    {
        JsonObject definitions = _document["definitions"]!.AsObject();
        string? name;
        if (_options.ResponseSchemaNames.TryGetValue(method, out string? configured)) name = configured;
        else
        {
            string? parameterName = parameter?["$ref"]?.GetValue<string>().Split('/').Last();
            name = parameterName?.EndsWith("Params", StringComparison.Ordinal) == true ? parameterName[..^6] + "Response" : CSharpNames.Identifier(method) + "Response";
            _diagnostics.Add("Inferred " + method + " result as " + name + " by naming convention; ResponseSchemaNames can override this association.");
        }
        if (!definitions.ContainsKey(name))
            throw new ArgumentException("Cannot resolve response schema for '" + method + "' (expected " + name + "). Supply supplementary definitions and/or ResponseSchemaNames. A request is never inferred to be a notification.");
        return new JsonObject { ["$ref"] = "#/definitions/" + name.Replace("~", "~0").Replace("/", "~1") };
    }
}
