using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Soenneker.Contexor.Builder;

/// <summary>Generated C# source and project that can be inspected before writing it to disk.</summary>
public sealed record ContexorBuildResult(
    IReadOnlyDictionary<string, string> Files,
    string ClientType,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>NuGet dependencies included in the generated project.</summary>
    public IReadOnlyDictionary<string, string> RequiredPackages { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        ["Soenneker.Asyncs.Semaphores"] = "4.0.6",
        ["Soenneker.Extensions.Object"] = "4.0.4349",
        ["Soenneker.Extensions.String"] = "4.0.745",
        ["Soenneker.Extensions.Task"] = "4.0.128",
        ["Soenneker.Extensions.ValueTask"] = "4.0.121",
        ["Soenneker.Utils.MemoryStream"] = "4.0.1543"
    });
}

