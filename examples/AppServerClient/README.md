# App-server client example

References the generated `CodexClient.csproj` directly. Defaults to `C:\git\codex\output` when present, otherwise the existing `C:\codex\output`. The generated namespace must be `Soenneker`.

From the builder repository:

```powershell
dotnet run --project examples/AppServerClient
```

Override the library location and/or executable:

```powershell
dotnet run --project examples/AppServerClient -p:GeneratedClientProject=C:/git/codex/output/CodexClient.csproj -- "C:\path\to\codex.exe"
```

The program starts `codex app-server --listen stdio://`, sends `initialize` and `initialized`, calls `model/list` and `thread/list`, and prints the results. It uses your existing Codex login. It does not start a model turn. Calls have a 60-second timeout; Ctrl+C cancels them. The child server is shut down before exit.

Protocol reference: [Codex app-server documentation](https://learn.chatgpt.com/docs/app-server).

This example stays outside the solution so normal builds do not depend on a machine-local generated library.
