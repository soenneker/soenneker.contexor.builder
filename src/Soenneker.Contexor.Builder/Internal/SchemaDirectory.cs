using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Contexor.Builder.Internal;

internal static class SchemaDirectory
{
    internal static async ValueTask<IEnumerable<string>> Files(string input, string output, IDirectoryUtil directoryUtil, CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        var pending = new Stack<string>();
        pending.Push(input);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string outputRoot = Path.TrimEndingDirectorySeparator(output);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string file in await directoryUtil.GetFilesByExtension(directory, string.Empty, cancellationToken: cancellationToken).NoSync())
            {
                if (Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase)) paths.Add(file);
            }
            foreach (string child in await directoryUtil.GetAllDirectories(directory, cancellationToken).NoSync())
            {
                string name = Path.GetFileName(child);
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    child.Equals(outputRoot, comparison) || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(child);
            }
        }
        return paths.OrderBy(p => p, StringComparer.Ordinal);
    }

    internal static JsonObject Definitions(string json, string path)
    {
        JsonObject root = JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException($"Expected a JSON Schema object in {path}.");
        JsonObject definitions = root["definitions"] is JsonObject bundled ? (JsonObject)bundled.DeepClone() : new JsonObject();
        bool standalone = !root.ContainsKey("definitions") || new[] { "properties", "required", "oneOf", "anyOf", "allOf", "$ref", "enum", "const", "items" }.Any(root.ContainsKey);
        if (standalone)
        {
            if (!IsSchema(root)) throw new ArgumentException($"Expected a JSON Schema in {path}.");
            string name = root["title"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path);
            root.Remove("definitions");
            definitions[name] = root;
        }
        return definitions;
    }

    internal static bool IsSchema(JsonObject schema) => schema.Count == 0 ||
        new[] { "$ref", "$schema", "type", "title", "description", "properties", "required", "oneOf", "anyOf", "allOf", "enum", "const", "items", "additionalProperties" }.Any(schema.ContainsKey);
}
