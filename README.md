# Suterusu (Next)

> [!WARNING]
> This is a rewrite of [Suterusu](https://github.com/HachiwareSoftware/Suterusu), focused on stablity and coding experience. While this rewrite is aimed at these purposes, not all features are tested or properly implemented, use at your own risk.

Communicates with AI using your system clipboard (Windows only).

## Features

+ Vibe-coded with a handful of agents (GPT-5.4, Claude Sonnet 4.6, Qwen 3.5 Plus, ~~MiniMax M2.5 Free~~) (im too poor omg).
+ Compatible with any OpenAI-like API endpoints.
+ Built-in ChatGPT / Codex browser login through a bundled local `CLIProxyAPI` sidecar.
+ Model fallback support (automatically tries backup models if primary fails)
+ It works on my machine.

## Usage

### First time

To open the settings UI (which you have to in order to configure), launch with `--open-settings` argument. After configuration, you can launch the app normally.

On first run, if no chat target is configured yet, the app now opens Settings automatically.

If you need debugging, enable it with `--debug` and it should show a console when launching.

Keybinds:
- F6: Clear chat history
- F7: Send clipboard content to AI
- F8: Copy AI response to clipboard
- F12: Close the application

## ChatGPT / Codex Login

Suterusu now bundles `CLIProxyAPI` for Windows x64 and ARM64 under `tools/cliproxy/` and can use it as a local OpenAI-compatible bridge.

Recommended flow:
- Launch `Suterusu.exe --open-settings`
- Go to the `Chat` tab
- In `ChatGPT / Codex (CLI Proxy)`, click `Connect ChatGPT & Use`
- Complete browser OAuth
- Let the app start the local proxy, detect models, test the connection, and save the generated local endpoint into `Model Priority`

After setup, the local endpoint is typically:
- `http://127.0.0.1:8317/v1`

The proxy runtime is installed on first use under:
- `%LocalAppData%\Suterusu\CLIProxyAPI`

## Build

Main app:

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "Suterusu\Suterusu.csproj" /t:Restore,Build /p:Configuration=Debug /v:minimal
```

Test project:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" build "Suterusu.Tests\Suterusu.Tests.csproj" `
  --configuration Debug -p:BuildProjectReferences=false -v minimal

& "C:\Program Files\dotnet\dotnet.exe" test "Suterusu.Tests\Suterusu.Tests.csproj" `
  --no-build --configuration Debug -p:BuildProjectReferences=false -v minimal
```

Notes:
- The app project is old-style `.NET Framework 4.8`, so `MSBuild.exe` is still used for the main app.
- The test project is SDK-style and is run through `dotnet`.

## Review Notes

Detailed implementation notes for the CLI proxy integration live in:
- `docs/cli-proxy-integration.md`

## License

[MIT](./LICENSE)
