using System.Collections.Generic;

namespace Soenneker.Contexor.Builder;

/// <summary>Names and policies for generated .NET 10 JSON-RPC 2.0 clients.</summary>
public sealed class ContexorBuilderOptions
{
    /// <summary>The generated root namespace. Each segment must be a C# identifier.</summary>
    public string Namespace { get; init; } = "Generated.Client";

    /// <summary>The generated client class name.</summary>
    public string ClientName { get; init; } = "RpcClient";

    /// <summary>Reject schemas requiring an untyped JsonElement fallback. Defaults to true.</summary>
    public bool FailOnUntypedSchemas { get; init; } = true;

    /// <summary>Allows replacing generated files with matching names. Other files are left untouched.</summary>
    public bool Overwrite { get; init; }

    /// <summary>Supplementary draft-7 bundles. Missing definitions are imported; the primary document wins.</summary>
    public IReadOnlyList<string> SupplementalSchemaJson { get; init; } = [];

    /// <summary>Explicit method-to-response definition names for protocol bundles. Otherwise Params/Request to Response naming is inferred.</summary>
    public IReadOnlyDictionary<string, string> ResponseSchemaNames { get; init; } = new Dictionary<string, string>();

    /// <summary>Omit the JSON-RPC version member for protocols such as Codex app-server. Applies only to protocol schema bundles.</summary>
    public bool OmitJsonRpcVersion { get; init; }
}
