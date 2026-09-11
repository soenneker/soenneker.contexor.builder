using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Contexor.Builder.Abstract;
using Soenneker.Tests.HostedUnit;
using Soenneker.Tests.Attributes.Local;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.Process.Abstract;

namespace Soenneker.Contexor.Builder.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class ContexorBuilderTests : HostedUnitTest
{
    private readonly IContexorBuilder _builder;

    private readonly IFileUtil _fileUtil;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IProcessUtil _processUtil;

    private Task<string> Definition =>
        _fileUtil.Read(Path.Combine(AppContext.BaseDirectory, "codex_app_server_protocol.v2.schemas.json"));

    private Task<string> Supplement =>
        _fileUtil.Read(Path.Combine(AppContext.BaseDirectory, "codex_app_server_protocol.schemas.json"));

    private async Task<ContexorBuilderOptions> GetOptions() => new()
    {
        Namespace = "Example", ClientName = "CodexClient", OmitJsonRpcVersion = true,
        SupplementalSchemaJson = [await Supplement],
        ResponseSchemaNames = new Dictionary<string, string>
        {
            ["config/value/write"] = "ConfigWriteResponse", ["config/batchWrite"] = "ConfigWriteResponse",
            ["config/mcpServer/reload"] = "McpServerRefreshResponse",
            ["windowsSandbox/readiness"] = "WindowsSandboxReadinessResponse",
            ["account/logout"] = "LogoutAccountResponse", ["account/rateLimits/read"] = "GetAccountRateLimitsResponse",
            ["account/usage/read"] = "GetAccountTokenUsageResponse",
            ["configRequirements/read"] = "ConfigRequirementsReadResponse"
        }
    };

    public ContexorBuilderTests(Host host) : base(host)
    {
        _builder = Resolve<IContexorBuilder>(true);
        _fileUtil = Resolve<IFileUtil>(true);
        _directoryUtil = Resolve<IDirectoryUtil>(true);
        _processUtil = Resolve<IProcessUtil>(true);
    }

    [Test]
    [LocalOnly]
    public async Task Generate_from_local_directory()
    {
        const string inputDirectory = @"C:\codex\schemas";
        const string outputDirectory = @"C:\codex\output";

        var options = new ContexorBuilderOptions
        {
            Namespace = "Soenneker", ClientName = "CodexClient", OmitJsonRpcVersion = true,
            Overwrite = true, ResponseSchemaNames = (await GetOptions()).ResponseSchemaNames
        };

        ContexorBuildResult result = await _builder.GenerateDirectory(inputDirectory, outputDirectory, options);
        Console.WriteLine($"Generated {result.Files.Count} files at {Path.GetFullPath(outputDirectory)}.");
    }

    [Test]
    public async Task Schema_directory_outputs_source_and_project()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "contexor-directory-" + Guid.NewGuid().ToString("N"));
        await _directoryUtil.Create(temporary);
        try
        {
            string input = Path.Combine(temporary, "schemas");
            string output = Path.Combine(temporary, "generated");
            await _directoryUtil.Create(Path.Combine(input, "companion"));
            await _fileUtil.Write(Path.Combine(input, "v2.json"), await Definition);
            await _fileUtil.Write(Path.Combine(input, "companion", "full.json"), await Supplement);
            var options = new ContexorBuilderOptions
            {
                Namespace = "Example", ClientName = "CodexClient", OmitJsonRpcVersion = true,
                Overwrite = true, ResponseSchemaNames = (await GetOptions()).ResponseSchemaNames
            };
            ContexorBuildResult result = await _builder.GenerateDirectory(input, output, options);
            await Assert.That(result.Files.ContainsKey("CodexClient.cs")).IsTrue();
            await Assert.That(result.Files.ContainsKey("RpcJsonContext.cs")).IsTrue();
            foreach ((string relative, string source) in result.Files)
            {
                await Assert.That((relative.EndsWith(".cs", StringComparison.Ordinal) || relative == "CodexClient.csproj")).IsTrue();
                await Assert.That(await _fileUtil.Read(Path.Combine(output, relative))).IsEqualTo(source);
            }

            ContexorBuildResult expected = _builder.Generate(await Definition, await GetOptions());
            await Assert.That(result.Files.Count).IsEqualTo(expected.Files.Count);
            await Assert.That(result.Files.All(p => expected.Files[p.Key] == p.Value)).IsTrue();
            Console.WriteLine($"Generated and verified {result.Files.Count} files at {Path.GetFullPath(output)}.");
        }
        finally
        {
            await _directoryUtil.Delete(temporary);
        }
    }

    [Test]
    public async Task Schema_directory_loads_individual_schemas_and_excludes_output()
    {
        string input = Path.Combine(Path.GetTempPath(), "contexor-individual-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(input, "generated");
        await _directoryUtil.Create(output);
        await _directoryUtil.Create(Path.Combine(input, "models"));
        try
        {
            await _fileUtil.Write(Path.Combine(input, "ClientRequest.json"),
                """{"title":"ClientRequest","oneOf":[{"type":"object","required":["id","method","params"],"properties":{"id":{"type":"string"},"method":{"enum":["ping"]},"params":{"$ref":"#/definitions/PingParams"}}}]}""");
            await _fileUtil.Write(Path.Combine(input, "models", "PingParams.json"),
                """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");
            await _fileUtil.Write(Path.Combine(input, "models", "response.json"),
                """{"title":"PingResponse","type":"object","properties":{"value":{"type":"integer"}},"required":["value"]}""");
            await _fileUtil.Write(Path.Combine(output, "unrelated.json"), "not a schema");
            ContexorBuildResult result = await _builder.GenerateDirectory(input, output);
            await Assert.That(result.Files.ContainsKey("Models/PingParams.cs")).IsTrue();
            await Assert.That(result.Files.ContainsKey("Models/PingResponse.cs")).IsTrue();
            await Assert.That(await _fileUtil.Read(Path.Combine(output, "unrelated.json"))).IsEqualTo("not a schema");
            await Assert.That(async () => await _builder.GenerateDirectory(input, output)).Throws<IOException>();
        }
        finally
        {
            await _directoryUtil.Delete(input);
        }
    }

    [Test]
    public async Task Codex_client_compiles_and_exchanges_bidirectional_messages()
    {
        string directory = Path.Combine(Path.GetTempPath(), "contexor-codex-" + Guid.NewGuid().ToString("N"));
        await _directoryUtil.Create(directory);
        try
        {
            string input = Path.Combine(directory, "input.json");
            await _fileUtil.Write(input, await Definition);
            ContexorBuildResult result = await _builder.GenerateFile(input, Path.Combine(directory, "Generated"), await GetOptions());
            await _fileUtil.Write(Path.Combine(directory, "Consumer.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Program.cs" />
                    <ProjectReference Include="Generated/CodexClient.csproj" />
                  </ItemGroup>
                </Project>
                """);
            await _fileUtil.Write(Path.Combine(directory, "Program.cs"),
                await _fileUtil.Read(Path.Combine(AppContext.BaseDirectory, "ConsumerProgram.txt")));
            await _fileUtil.Write(Path.Combine(directory, "supplement.json"), await Supplement);
            string output = await RunProcess("dotnet", directory, "run --project Consumer.csproj --verbosity quiet -p:TreatWarningsAsErrors=true",
                TimeSpan.FromMinutes(3));
            await Assert.That(output).Contains("All Codex consumer checks passed");
            if (Environment.GetEnvironmentVariable("CONTEXOR_NATIVE_AOT") == "1")
            {
                await _fileUtil.Write(Path.Combine(directory, "Program.cs"),
                    await _fileUtil.Read(Path.Combine(AppContext.BaseDirectory, "NativeAotConsumer.txt")));
                string published = Path.Combine(directory, "native");
                await RunProcess("dotnet", directory,
                    $"publish Consumer.csproj -c Release -r {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier} " +
                    "-p:PublishAot=true -p:TrimmerSingleWarn=false -p:ILLinkTreatWarningsAsErrors=true " +
                    "-p:IlcTreatWarningsAsErrors=true -o native -v quiet");
                output += await RunProcess(
                    Path.Combine(published, OperatingSystem.IsWindows() ? "Consumer.exe" : "Consumer"), directory);
                await Assert.That(output).Contains("Native AOT consumer checks passed");
            }

            Console.WriteLine(output);
            if (Environment.GetEnvironmentVariable("CONTEXOR_GENERATED_OUTPUT") is { Length: > 0 } artifactDirectory)
            {
                foreach ((string relative, string contents) in result.Files)
                {
                    string path = Path.Combine(artifactDirectory, relative);
                    await _directoryUtil.Create(Path.GetDirectoryName(path)!);
                    await _fileUtil.Write(path, contents);
                }

                await _fileUtil.Write(Path.Combine(artifactDirectory, "verification.txt"),
                    $"Generated {result.Files.Count} files.\n" + output);
            }
        }
        finally
        {
            string resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Unsafe cleanup path.");
            await _directoryUtil.Delete(resolved);
        }
    }

    private async Task<string> RunProcess(string executable, string directory, string arguments = "",
        TimeSpan? timeout = null)
    {
        List<string> output = await _processUtil.Start(executable, workingDirectory: directory, arguments: arguments,
            waitForExit: true, timeout: timeout ?? TimeSpan.FromMinutes(5));
        return string.Join(Environment.NewLine, output);
    }

    [Test]
    public async Task Generation_is_deterministic_and_emits_source_and_project()
    {
        ContexorBuildResult first = _builder.Generate(await Definition, await GetOptions());
        ContexorBuildResult second = _builder.Generate(await Definition, await GetOptions());
        await Assert.That(first.Files.All(x => (x.Key.EndsWith(".cs", StringComparison.Ordinal) || x.Key == "CodexClient.csproj"))).IsTrue();
        await Assert.That(first.Files.All(x => second.Files[x.Key] == x.Value)).IsTrue();
        await Assert.That(first.Diagnostics.Any(x => x.Contains("uses JsonElement"))).IsFalse();
        await Assert.That(first.ClientType).IsEqualTo("global::Example.CodexClient");
        var project = System.Xml.Linq.XDocument.Parse(first.Files["CodexClient.csproj"]);
        var packages = project.Descendants("PackageReference").ToDictionary(p => p.Attribute("Include")!.Value, p => p.Attribute("Version")!.Value);
        await Assert.That(packages.Count).IsEqualTo(first.RequiredPackages.Count);
        foreach ((string package, string version) in first.RequiredPackages)
            await Assert.That(packages[package]).IsEqualTo(version);
        await Assert.That(first.RequiredPackages.ContainsKey("Soenneker.Asyncs.Semaphores")).IsTrue();
        await Assert.That(first.Files["CodexClient.cs"].Contains(".NoSync()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(first.Files.ContainsKey("CodexClient.cs")).IsTrue();
        await Assert.That(first.Files.ContainsKey("Abstract/ICodexClient.cs")).IsTrue();
        await Assert.That(first.Files.ContainsKey("Abstract/IJsonRpcTransport.cs")).IsTrue();
        foreach ((string path, string contents) in first.Files.Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var declarations = System.Text.RegularExpressions.Regex.Matches(contents,
                @"(?m)^\s*(?:public|internal|private) (?:sealed |abstract |readonly |static |partial )*(?:class|record|struct|enum|interface) (\w+)");
            await Assert.That(declarations.Count).IsEqualTo(1);
            await Assert.That(path.StartsWith("Abstract/", StringComparison.Ordinal))
                        .IsEqualTo(contents.Contains("public interface ", StringComparison.Ordinal));
            await Assert.That(Path.GetFileNameWithoutExtension(path)).IsEqualTo(declarations[0].Groups[1].Value);
        }
        string source = string.Join("\n", first.Files.Values);
        foreach (string forbidden in new[]
                 {
                     "reader.GetString() switch", "value.ToString()",
                     "System.Activator", "MakeGenericType", "GetGenericArguments", "System.Reflection",
                     "DefaultJsonTypeInfoResolver", "populateMissingResolver", "UnionSchema", "System.Text.Json.Nodes"
                 })
            await Assert.That(source.Contains(forbidden, StringComparison.Ordinal)).IsFalse();
        await Assert.That(first.Files.ContainsKey("RpcJsonContext.cs")).IsTrue();
        await Assert.That(first.Files.ContainsKey("UnionMatchers.cs")).IsTrue();
        Console.WriteLine(
            $"Generated {first.Files.Count} files; {first.Diagnostics.Count} reported response associations.");
    }

    [Test]
    public async Task File_generation_preserves_existing_files_and_supports_explicit_overwrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), "contexor-files-" + Guid.NewGuid().ToString("N"));
        await _directoryUtil.Create(directory);
        // A small real protocol verifies file behavior without writing the complete Codex fixture repeatedly.
        const string schema =
            """{"definitions":{"ClientRequest":{"oneOf":[{"type":"object","required":["id","method"],"properties":{"id":{"type":"string"},"method":{"enum":["ping"]}}}]},"PingResponse":{"type":"object"}}}""";
        try
        {
            string input = Path.Combine(directory, "schema.json");
            await _fileUtil.Write(input, schema);
            await _fileUtil.Write(Path.Combine(directory, "UserCode.cs"), "// Keep me");
            await _builder.GenerateFile(input, directory);
            await Assert.That(async () => await _builder.GenerateFile(input, directory)).Throws<IOException>();
            await _fileUtil.Write(Path.Combine(directory, "RpcClient.cs"), "// stale");
            await _fileUtil.Write(Path.Combine(directory, "RpcClient.csproj"), "<!-- stale -->");
            ContexorBuildResult result = await _builder.GenerateFile(input, directory, new ContexorBuilderOptions { Overwrite = true });
            await Assert.That(await _fileUtil.Read(Path.Combine(directory, "RpcClient.cs")))
                        .IsEqualTo(result.Files["RpcClient.cs"]);
            await Assert.That(await _fileUtil.Read(Path.Combine(directory, "RpcClient.csproj")))
                        .IsEqualTo(result.Files["RpcClient.csproj"]);
            await Assert.That(await _fileUtil.Read(Path.Combine(directory, "UserCode.cs"))).IsEqualTo("// Keep me");
            await Assert.That(await _fileUtil.Read(input)).IsEqualTo(schema);
        }
        finally
        {
            string resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Unsafe cleanup path.");
            await _directoryUtil.Delete(resolved);
        }
    }

    [Test]
    public async Task Incomplete_contracts_fail_instead_of_inventing_results()
    {
        string definition = await Definition;
        await Assert.That(() => _builder.Generate(definition)).Throws<ArgumentException>();
        await Assert.That(() => _builder.Generate("null")).Throws<ArgumentException>();
        await Assert.That(() => _builder.Generate("""{"openrpc":"1.2.6","methods":[]}""")).Throws<ArgumentException>();
        await Assert.That(() => _builder.Generate(definition, cancellationToken: new CancellationToken(true)))
                    .Throws<OperationCanceledException>();
    }
}