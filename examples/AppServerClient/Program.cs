using System.Diagnostics;
using Soenneker;
using Soenneker.Models;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;

// Pass the Codex executable as the first argument, or use the installed CLI on PATH.
string executable = args.Length > 0 ? args[0] : "codex";
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; timeout.Cancel(); };
using var server = new Process
{
    StartInfo = new ProcessStartInfo(executable)
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }
};
server.StartInfo.ArgumentList.Add("app-server");
server.StartInfo.ArgumentList.Add("--listen");
server.StartInfo.ArgumentList.Add("stdio://");

try
{
    server.Start();
}
catch (Exception error)
{
    Console.Error.WriteLine($"Could not start {executable}: {error.Message}");
    return 1;
}

// Drain stderr independently so server logging cannot block the JSON-RPC connection.
Task<string> stderr = server.StandardError.ReadToEndAsync();
bool succeeded = false;
try
{
    await using var transport = new StreamJsonRpcTransport(
        server.StandardOutput.BaseStream, server.StandardInput.BaseStream);
    await using var client = new CodexClient(transport);
    var initialized = await client.Initialize(new InitializeParams
    {
        ClientInfo = new ClientInfo { Name = "contexor_example", Version = "1.0.0" }
    }, timeout.Token).NoSync();
    await client.Initialized(timeout.Token).NoSync();
    Console.WriteLine($"Connected: {initialized.UserAgent}");

    var models = await client.ModelList(new ModelListParams { Limit = 5 }, timeout.Token).NoSync();
    Console.WriteLine($"Models ({models.Data.Count}):");
    foreach (var model in models.Data)
        Console.WriteLine($"  {model.Id}: {model.DisplayName}");

    var threads = await client.ThreadList(new ThreadListParams { Limit = 5 }, timeout.Token).NoSync();
    Console.WriteLine($"Threads returned: {threads.Data.Count}");
    succeeded = true;
}
catch (Exception error)
{
    Console.Error.WriteLine($"App-server call failed: {error.Message}");
}
finally
{
    // EOF requests shutdown. Kill only this child process tree if it does not exit promptly.
    server.StandardInput.Close();
    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try { await server.WaitForExitAsync(shutdown.Token).NoSync(); }
    catch (OperationCanceledException)
    {
        if (!server.HasExited) server.Kill(entireProcessTree: true);
        await server.WaitForExitAsync().NoSync();
    }
    string diagnostics = await stderr.NoSync();
    if (!succeeded && diagnostics.Length > 0) Console.Error.WriteLine(diagnostics);
}
return succeeded ? 0 : 1;
