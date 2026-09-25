using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Linq;
using System.Collections.Generic;
using Soenneker.Contexor.Builder.Abstract;
using Soenneker.Contexor.Builder.Internal;
using Soenneker.JsonSchema.ToCSharp.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Contexor.Builder;

public sealed class ContexorBuilder(IFileUtil fileUtil, IDirectoryUtil directoryUtil, IJsonSchemaToCSharp jsonSchemaToCSharp) : IContexorBuilder
{
    public ContexorBuildResult Generate(string schemaJson, ContexorBuilderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        schemaJson.ThrowIfNullOrWhiteSpace(nameof(schemaJson));
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new ContexorBuilderOptions();
        JsonObject document = JsonNode.Parse(schemaJson) as JsonObject ?? throw new ArgumentException("Expected a JSON definition object.");
        return new ProtocolEmitter(document, options, jsonSchemaToCSharp, cancellationToken).Generate();
    }

    public async ValueTask<ContexorBuildResult> GenerateFile(string schemaPath, string outputDirectory,
        ContexorBuilderOptions? options = null, CancellationToken cancellationToken = default)
    {
        schemaPath.ThrowIfNullOrWhiteSpace(nameof(schemaPath));
        outputDirectory.ThrowIfNullOrWhiteSpace(nameof(outputDirectory));
        string output = Path.GetFullPath(outputDirectory);
        string json = await fileUtil.Read(schemaPath, cancellationToken: cancellationToken).NoSync();
        ContexorBuildResult result = Generate(json, options, cancellationToken);
        await WriteFiles(result, output, options, [Path.GetFullPath(schemaPath)], cancellationToken).NoSync();
        return result;
    }

    public async ValueTask<ContexorBuildResult> GenerateDirectory(string schemaDirectory, string outputDirectory,
        ContexorBuilderOptions? options = null, CancellationToken cancellationToken = default)
    {
        schemaDirectory.ThrowIfNullOrWhiteSpace(nameof(schemaDirectory));
        outputDirectory.ThrowIfNullOrWhiteSpace(nameof(outputDirectory));
        cancellationToken.ThrowIfCancellationRequested();
        string input = Path.GetFullPath(schemaDirectory);
        string output = Path.GetFullPath(outputDirectory);
        var inputs = new List<(string Path, JsonObject Definitions, int FlatCount)>();
        foreach (string path in await SchemaDirectory.Files(input, output, directoryUtil, cancellationToken).NoSync())
        {
            string json = await fileUtil.Read(path, cancellationToken: cancellationToken).NoSync();
            JsonObject definitions = SchemaDirectory.Definitions(json, path);
            int flatCount = definitions.Count(p => p.Value is JsonObject obj && SchemaDirectory.IsSchema(obj));
            inputs.Add((path, definitions, flatCount));
        }
        if (inputs.Count == 0) throw new ArgumentException("No JSON schemas found in " + input, nameof(schemaDirectory));
        (string Path, JsonObject Definitions, int FlatCount) primary = inputs.Where(p => p.Definitions.ContainsKey("ClientRequest"))
                                                                             .OrderByDescending(p => p.FlatCount).ThenBy(p => p.Path, StringComparer.Ordinal).FirstOrDefault();
        if (primary.Definitions == null) throw new ArgumentException("The schema directory must define ClientRequest.", nameof(schemaDirectory));
        var combined = new JsonObject();
        ProtocolEmitter.ImportDefinitions(primary.Definitions, combined);
        foreach ((string Path, JsonObject Definitions, int FlatCount) item in inputs.Where(p => p.Path != primary.Path).OrderByDescending(p => p.FlatCount).ThenBy(p => p.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProtocolEmitter.ImportDefinitions(item.Definitions, combined);
        }
        var document = new JsonObject { ["definitions"] = combined };
        ContexorBuildResult result = Generate(document.ToJsonString(), options, cancellationToken);
        result = result with { Diagnostics = result.Diagnostics.Append($"Loaded {inputs.Count} schema files; primary input: {primary.Path}. Other inputs supplement missing definitions.").ToArray() };
        await WriteFiles(result, output, options, inputs.Select(p => p.Path).ToArray(), cancellationToken).NoSync();
        return result;
    }

    private async ValueTask WriteFiles(ContexorBuildResult result, string output, ContexorBuilderOptions? options,
        IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        foreach (string relativePath in result.Files.Keys)
        {
            string path = Path.GetFullPath(Path.Combine(output, relativePath));
            if (inputs.Contains(path, StringComparer.OrdinalIgnoreCase))
                throw new IOException("Output would overwrite the input schema.");
            if ((await fileUtil.Exists(path, cancellationToken).NoSync()) && options?.Overwrite != true)
                throw new IOException($"Output file already exists: {path}. Set Overwrite to replace generated files.");
        }

        foreach ((string relativePath, string contents) in result.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(Path.Combine(output, relativePath));
            if (!path.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Generated path escaped the output directory.");
            if (options?.Overwrite == true)
            {
                await fileUtil.WriteAtomically(path, contents, cancellationToken: cancellationToken).NoSync();
                continue;
            }

            await directoryUtil.Create(Path.GetDirectoryName(path)!, cancellationToken: cancellationToken).NoSync();
            // IFileUtil writes replace existing files; CreateNew preserves the no-overwrite guarantee even during concurrent generation.
            await using var stream = new FileStream(path,
                FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(contents.AsMemory(), cancellationToken).NoSync();
        }

    }
}

