using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Contexor.Builder.Abstract;

/// <summary>Generates strongly typed JSON-RPC 2.0 clients from JSON Schema protocol bundles.</summary>
public interface IContexorBuilder
{
    /// <summary>Generates callable RPC methods, models, bidirectional transport source, and a project file in memory.</summary>
    /// <param name="schemaJson">A JSON Schema draft-7 bundle containing ClientRequest and optional notification/server-request definitions.</param>
    /// <param name="options">Names and generation settings.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    /// <returns>Generated relative file paths, contents, client type, and diagnostics.</returns>
    ContexorBuildResult Generate(string schemaJson, ContexorBuilderOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reads a schema file and writes C# source files and a project. Existing output files require the Overwrite option.</summary>
    /// <param name="schemaPath">Path to a JSON Schema protocol bundle; supplementary definitions may be supplied through options.</param>
    /// <param name="outputDirectory">The directory receiving generated source. Unrelated files are preserved.</param>
    /// <param name="options">Names and generation settings.</param>
    /// <param name="cancellationToken">Cancels file I/O and generation.</param>
    /// <returns>The generated source contents and diagnostics.</returns>
    ValueTask<ContexorBuildResult> GenerateFile(string schemaPath, string outputDirectory, ContexorBuilderOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Loads bundled and individual JSON schemas recursively from a directory and writes the generated C# files and project.</summary>
    /// <param name="schemaDirectory">The schema directory. Individual schemas use their title, or filename, as their definition name. References must be bundled local definition references.</param>
    /// <param name="outputDirectory">The output directory. Its subtree is excluded from input discovery, as are bin, obj, .git, and directory links.</param>
    /// <param name="options">Naming, response associations, and overwrite settings. The richest flat bundle defining ClientRequest is primary; other schemas supplement it.</param>
    /// <param name="cancellationToken">Cancels discovery, reading, generation, or writing.</param>
    /// <returns>The generated source and diagnostics identifying the selected primary input.</returns>
    ValueTask<ContexorBuildResult> GenerateDirectory(string schemaDirectory, string outputDirectory,
        ContexorBuilderOptions? options = null, CancellationToken cancellationToken = default);
}



