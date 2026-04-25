# CLI Proxy Integration Notes

## Summary

This change adds a bundled `CLIProxyAPI` sidecar so Suterusu can authenticate through browser-based ChatGPT / Codex OAuth and keep using the app's existing OpenAI-compatible request pipeline.

Final flow:

`Suterusu -> http://127.0.0.1:8317/v1 -> CLIProxyAPI -> ChatGPT / Codex`

## Why this approach

- Suterusu already speaks OpenAI-compatible `/chat/completions`.
- Reusing that contract keeps the app-side change small.
- Browser OAuth is delegated to `CLIProxyAPI` instead of reimplementing OAuth inside the app.
- The end-user setup becomes one primary button in Settings: `Connect ChatGPT & Use`.

## User-facing behavior

### First-run behavior

- `Program.cs` now checks whether the app has any chat target configured.
- If `config.json` did not already exist and no model or CLI proxy target is configured, Settings is opened automatically.

### Settings UI

The `Chat` tab now contains a `ChatGPT / Codex (CLI Proxy)` section with:

- `Connect ChatGPT & Use`
- `Start`
- `Stop`
- `Test`
- `Detect Models`
- `Enable CLI proxy for chat`
- `Auto-start with app`
- preferred model field
- read-only local endpoint field
- status text

`Connect ChatGPT & Use` performs the full assisted flow:

1. Build config from current UI state.
2. Enable CLI proxy.
3. Run browser OAuth via `cli-proxy-api.exe --codex-login`.
4. Start the local proxy.
5. Query `/v1/models`.
6. Pick a valid model if the configured one is unavailable.
7. Send a test `/v1/chat/completions` request.
8. Upsert the local proxy entry into `ModelPriority`.
9. Save config and refresh runtime configuration.

## New configuration model

### `CliProxySettings`

Added to `AppConfig` as `CliProxy`.

Key fields:

- `Enabled`
- `AutoStart`
- `ExecutablePath`
- `RuntimeDirectory`
- `ConfigPath`
- `AuthDirectory`
- `Host`
- `Port`
- `ApiKey`
- `ManagementKey`
- `Model`
- `OAuthCallbackPort`

Defaults are created by `CliProxySettings.CreateDefault()`.

### `AppConfig.Normalize()`

`NormalizeCliProxySettings()` now ensures:

- runtime/config/auth paths are populated
- host stays local-only (`127.0.0.1`, `localhost`, `::1`)
- ports are clamped into valid ranges
- model has a fallback default
- API keys are generated if missing
- management key is regenerated if it matches the API key

### `AppConfig.Validate()`

When CLI proxy is enabled, validation now requires:

- local-only host
- valid proxy port
- valid OAuth callback port
- non-empty model
- non-empty API key
- non-empty management key

## New services

### `CliProxyConfigWriter`

Responsibility:

- write `config.yaml` for `CLIProxyAPI`
- create parent directories
- force local-only settings
- emit local API key + management secret

Important generated YAML includes:

- `host`
- `port`
- `auth-dir`
- `api-keys`
- `remote-management.allow-remote: false`
- `remote-management.disable-control-panel: true`

### `CliProxyHttpClient`

Responsibility:

- wait until proxy is healthy
- call `GET /v1/models`
- send a test `POST /v1/chat/completions`

Return types:

- `CliProxyResult`
- `CliProxyHealthResult`

### `CliProxyProcessManager`

Responsibility:

- locate bundled `CLIProxyAPI` binary under `tools/cliproxy/<arch>`
- install it into `%LocalAppData%\Suterusu\CLIProxyAPI\bin`
- copy shipping metadata (`LICENSE`, `VERSION.txt`, `checksums.txt`)
- run browser OAuth login
- start and stop the local proxy process
- expose simple health/test helpers

Notable implementation details:

- argument quoting is implemented explicitly for .NET Framework process launching
- startup waits for readiness before returning success
- browser login has a 10-minute timeout
- owned proxy process is tracked and disposed by the app
- architecture detection prefers `PROCESSOR_ARCHITEW6432` / `PROCESSOR_ARCHITECTURE`

## Runtime wiring

### `HeadlessApplicationContext`

Changes:

- constructs `CliProxyProcessManager`
- auto-starts proxy on background task when `CliProxy.Enabled && CliProxy.AutoStart`
- logs current CLI proxy status in startup banner
- disposes proxy manager on shutdown

### `Program.cs`

Changes:

- keeps explicit `--open-settings` flow
- adds first-run settings redirect when no chat target is configured yet

## Model Priority integration

The app still routes all inference through `ModelPriority`.

`SettingsWindow.UpsertCliProxyModelEntry()` ensures the CLI proxy entry is kept at the top of the list and has:

- `Name = "ChatGPT (CLIProxyAPI)"`
- `BaseUrl = "http://127.0.0.1:<port>/v1"`
- `ApiKey = CliProxy.ApiKey`
- `Model = CliProxy.Model`

This avoids special-casing the main `AiClient` pipeline.

## Bundled assets

Bundled from official release:

- repository: `router-for-me/CLIProxyAPI`
- version: `v6.9.38`

Added directories:

- `tools/cliproxy/windows-x64/`
- `tools/cliproxy/windows-arm64/`

Each bundle includes:

- `cli-proxy-api.exe`
- upstream `LICENSE`
- `VERSION.txt`
- `checksums.txt`

## Build and test notes

### Build

Main app is built with Windows `MSBuild.exe` because it is an old-style `.NET Framework 4.8` WPF/WinForms project.

`Suterusu.csproj` now includes:

- `Microsoft.NETFramework.ReferenceAssemblies.net48`
- `AutomaticallyUseReferenceAssemblyPackages=true`

This reduces dependence on machine-global targeting pack setup.

### Test

`Suterusu.Tests` remains SDK-style and is run through `dotnet`.

Reliable commands used during verification:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "Suterusu\Suterusu.csproj" /t:Restore,Build /p:Configuration=Debug /v:minimal

& "C:\Program Files\dotnet\dotnet.exe" build "Suterusu.Tests\Suterusu.Tests.csproj" `
  --configuration Debug -p:BuildProjectReferences=false -v minimal

& "C:\Program Files\dotnet\dotnet.exe" test "Suterusu.Tests\Suterusu.Tests.csproj" `
  --no-build --configuration Debug -p:BuildProjectReferences=false -v minimal
```

Verified result at the time of this change:

- app build succeeded
- tests passed: `95 / 95`

## Files added or updated

Core code:

- `Suterusu/Configuration/CliProxySettings.cs`
- `Suterusu/Configuration/AppConfig.cs`
- `Suterusu/Models/CliProxyResult.cs`
- `Suterusu/Models/CliProxyHealthResult.cs`
- `Suterusu/Services/CliProxyConfigWriter.cs`
- `Suterusu/Services/CliProxyHttpClient.cs`
- `Suterusu/Services/CliProxyProcessManager.cs`
- `Suterusu/Application/HeadlessApplicationContext.cs`
- `Suterusu/UI/SettingsWindow.xaml`
- `Suterusu/UI/SettingsWindow.xaml.cs`
- `Suterusu/Program.cs`
- `Suterusu/Suterusu.csproj`

Tests:

- `Suterusu.Tests/ConfigTests.cs`
- `Suterusu.Tests/ResultTypeTests.cs`

Bundled assets:

- `tools/cliproxy/windows-x64/*`
- `tools/cliproxy/windows-arm64/*`

Docs:

- `README.md`
- `docs/cli-proxy-integration.md`

## Reviewer checklist

- Confirm first-run Settings redirect is acceptable UX.
- Confirm bundling third-party executable in-repo is acceptable for release workflow.
- Confirm `CLIProxyAPI` version pin `v6.9.38` is acceptable.
- Confirm local-only proxy config defaults match project security expectations.
- Confirm `ModelPriority` auto-upsert behavior is the intended product behavior.
