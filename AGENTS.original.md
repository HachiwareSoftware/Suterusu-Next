# AGENTS.md

Repository notes for agents working on Suterusu-Next.

## Project summary

Suterusu-Next is a Windows background utility written mostly in C# on .NET Framework 4.8. It connects clipboard text, screenshots, OCR providers, browser CDP automation, and OpenAI-compatible chat providers through global hotkeys and a WPF settings window.

Default hotkeys:

- `F6`: clear chat history.
- `F7`: read clipboard and send it to AI.
- `F8`: copy the last AI response to the clipboard.
- `F12`: quit.
- `Shift+F7`: screenshot/OCR mode by default.

The app runs headless by default. Use `--open-settings` to open the settings UI and `--debug` to enable console logging. Runtime config is stored as `config.json` next to the executable.

## Important working rules

- This repository is Windows-only.
- Main app project `Suterusu/Suterusu.csproj` is an old-style .NET Framework project with explicit compile includes. Add new `.cs` files to the `.csproj` manually.
- Use Visual Studio MSBuild for the main solution. `dotnet build` is not reliable for the old-style main project.
- Use `dotnet test` for the SDK-style test project after building the solution.
- Running `Suterusu.exe` often locks `Suterusu/bin/Debug/Suterusu.exe`; stop the process before rebuilding if MSBuild copy fails.
- Leave untracked `.memory/` and `test_ocr.png` alone unless the user explicitly asks.
- Do not use destructive git commands. Do not amend unless explicitly requested.

## Build and test

Build Debug:

```powershell
& "E:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" "Suterusu-Next.sln" /t:Build /p:Configuration=Debug /v:minimal
```

Build Release:

```powershell
& "E:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" "Suterusu-Next.sln" /t:Build /p:Configuration=Release /v:minimal
```

Run tests after build:

```powershell
dotnet test "Suterusu.Tests\Suterusu.Tests.csproj" --no-build --configuration Debug
```

Current expected test count after the reasoning and OCR work is around `189` tests.

## .NET 10 Windows AI OCR helper

There is a separate helper project at `Suterusu.WindowsAiOcr/` targeting `net10.0-windows10.0.26100.0`. Visual Studio 2022 MSBuild 17.x cannot build .NET 10 SDK projects, so the helper is excluded from normal solution build in practice.

Build helper with local .NET 10 SDK if needed:

```powershell
& "$env:USERPROFILE\.dotnet10\dotnet.exe" build "Suterusu.WindowsAiOcr\Suterusu.WindowsAiOcr.csproj"
```

The helper output is expected at `Suterusu/bin/Debug/WindowsAiOcr/Suterusu.WindowsAiOcr.exe`.

## Repository layout

- `Suterusu/`: main Windows app, old-style .NET Framework 4.8 project.
- `Suterusu.Tests/`: xUnit test project targeting `net48`.
- `Suterusu.WindowsAiOcr/`: modern Windows OCR helper project.
- `docs/ocr/`: OCR mode documentation. `README.md` is the GitHub directory landing page.
- `docs/chat/reasoning_level.md`: chat reasoning level behavior.
- `.github/workflows/`: CI packaging/build workflows.

## Main architecture

`Program.cs` parses startup flags, configures logging, opens settings for `--open-settings`, or starts `HeadlessApplicationContext` for normal headless operation.

`HeadlessApplicationContext` owns the application lifetime and manually wires services. There is no IoC container. When adding a service, add a field, instantiate it in the constructor, refresh it if config changes, and dispose it in `Dispose(bool)`.

Core service graph:

- `ConfigManager`: loads/saves `config.json` and exposes current config.
- `AiClient`: OpenAI-compatible chat client with Sequential, RoundRobin, and Fastest dispatch.
- `ChatHistory`: bounded conversation history and vision-history helpers.
- `ClipboardAiController`: hotkey actions, clipboard send queue, OCR/screenshot flow, VLM screenshot flow.
- `KeyboardHook`: global low-level keyboard hook.
- `NotificationServiceFactory`: creates FlashWindow, CircleDot, or no-op notifications.
- `CdpService`: optional background CDP connector/script injector.
- `CliProxyProcessManager`: CLIProxyAPI install/start/login/model flow.

## Configuration

`AppConfig` and nested settings live in `Suterusu/Configuration/AppConfig.cs`. Config serialization uses `JsonSettings` with Newtonsoft.Json snake_case naming. Do not add `[JsonProperty]` unless there is a specific need.

When adding config fields:

1. Add property to the relevant POCO.
2. Add default in `CreateDefault()`.
3. Add trimming/clamping/migration in `Normalize()`.
4. Add validation in `Validate()` only when invalid config would break runtime.
5. Add tests in `ConfigTests.cs`.

`Normalize()` runs after load. `Save()` validates, normalizes, then writes JSON.

## Chat models

Chat model entries use `ModelEntry` and convert to `EndpointConfig` before requests.

Current important `ModelEntry` fields:

- `Name`
- `BaseUrl`
- `ApiKey`
- `Model`
- `Capability`: `Auto`, `TextOnly`, or `Vision`.
- `ReasoningEffort`: string, default `default`.

`Capability` is only used by VLM screenshot mode. Normal clipboard chat ignores it.

`ReasoningEffort` behavior:

- `default` omits `reasoning_effort` from requests.
- Any non-default value is sent as `reasoning_effort` exactly as configured.
- The UI displays title-case labels such as `Default` and `Custom...`, but stored/request values remain the underlying string.
- Fetch Models only uses explicit metadata fields and linked model detail documents for reasoning levels. It must not invent standard levels from provider names or from `supported_parameters` alone.
- Debug logs for this route are in `ModelPriorityEditor`: models URL, auth presence, response status, data count, direct/detail reasoning levels, final dropdown options.

## AiClient behavior

`AiClient.SendAsync` supports:

- `Sequential`: try entries in order.
- `RoundRobin`: start from `RoundRobinIndex`, advance on success.
- `Fastest`: race eligible endpoints and return first success.

`AiClient.SendVisionAsync` sends screenshots to vision-capable chat models for VLM Chat. It skips `TextOnly`, tries `Vision` and `Auto`, and treats image-unsupported errors as recoverable fallback cases.

OpenAI-compatible URLs append `/chat/completions` when needed. Avoid duplicating suffix handling in callers.

## OCR providers

OCR providers are in `OcrProvider`:

- `LlamaCpp`
- `Zai`
- `Custom`
- `HuggingFace`
- `PaddleX`
- `OneOcr`
- `VlmChat`
- `WindowsOcr`
- `WindowsAi`

Factory: `Suterusu/Services/OcrClientFactory.cs`.

Shared OCR flow is in `ClipboardAiController.ExecuteScreenOcrAsync()`:

1. Capture selected region as PNG bytes.
2. Optionally downscale with `ImageResizer`.
3. Resolve OCR prompt, optionally from clipboard.
4. If provider is `VlmChat`, call `ExecuteVlmChatAsync()`.
5. Otherwise call current `IOcrClient`.
6. Store result as `LastAiResponse` and notify.

Prompt behavior:

- Prompt-aware providers: llama.cpp, Z.ai, Hugging Face, Custom, VLM Chat.
- Direct OCR engines ignore prompt: PaddleX, OneOCR, Windows OCR, Windows AI OCR.

OCR docs live under `docs/ocr/README.md` and one provider file per mode.

### VLM Chat

`VlmChat` is an OCR provider mode that sends screenshots directly to configured chat vision models instead of OCR engines.

- Uses chat `SystemPrompt` as the system message.
- Uses OCR prompt as the image instruction.
- Stores history as text-only user turn: `[Image] <prompt>`.
- Does not store image bytes in history.
- Optional fallback can run another OCR provider after all VLM candidates fail.

### OneOCR

`OneOcr` uses Snipping Tool's `oneocr.dll`, `oneocr.onemodel`, and `onnxruntime.dll` through C# P/Invoke in-process.

- Requires 64-bit process.
- Auto-detects Snipping Tool package.
- Copies runtime files to app-local `OneOCR/` to avoid WindowsApps DLL loading ACL issues.
- P/Invoke wrapper is `OneOcrNative`; runtime detection is `OneOcrRuntimeLocator`.
- Keep all Win32 declarations in `NativeMethods.cs`.

### Windows OCR and Windows AI OCR

`WindowsOcr` uses `Windows.Media.Ocr` in-process and requires installed OCR languages.

`WindowsAi` uses the helper executable. It is intended for Copilot+ PC / NPU Windows AI APIs. If helper is missing, return a clear error.

### PaddleX

`PaddleX` posts to `{PaddleXUrl}/ocr` with:

- `file`: base64 PNG bytes.
- `fileType`: `1`.
- `visualize`: `false`.

Text is parsed from `result.ocrResults[*].prunedResult.rec_texts` or `recTexts`.

## CLIProxyAPI integration

CLIProxyAPI settings live in `CliProxySettings` and have a dedicated settings tab.

Supported provider modes:

- Codex / ChatGPT.
- Gemini.

Important behavior:

- CLIProxyAPI is installed/started by `CliProxyProcessManager`.
- Login arguments are built by `CliProxyProcessManager.BuildLoginArguments()`.
- Gemini login uses direct visible CLIProxyAPI interactive flow because CLIProxyAPI `--login` requires stdin prompts for login mode/project selection.
- Codex login keeps `--no-browser` flow where appropriate.
- Connect-and-use no longer smoke-tests a default model because Gemini model availability changes often.
- CLIProxyAPI must not auto-insert or override chat `ModelPriority` entries. It can be selected manually via endpoint preset.
- CLIProxyAPI endpoint preset uses `http://127.0.0.1:8317/v1/chat/completions` and the configured access key.

## CDP integration

CDP settings live in `CdpSettings`.

Important behavior:

- Connect only to already-running browser CDP. Do not launch a browser.
- Runs on background thread `Suterusu-CDP`.
- Logs to `logs/cdp-*.log` and also forwards to normal app logging.
- Connects to all injectable page targets, not only the first tab.
- Skips internal targets such as DevTools, `chrome://`, `edge://`, and `about:`.
- Script root defaults to `js/events`.
- Opening settings creates `js/events/onconnect` and `js/events/onload` folders.
- Event folders are named `onload`, `onconnect`, `onclick`, `onkeydown`, etc.
- Scripts are injected persistently with `Page.addScriptToEvaluateOnNewDocument` and evaluated on current pages.
- Disk script changes are detected by hashing; old persistent registrations are removed before replacing.
- Script wrapper executes immediately with an idempotency marker; it does not delay execution behind event listeners.

## Settings UI

Settings UI is WPF code-behind, not MVVM.

Important files:

- `SettingsWindow.xaml`
- `SettingsWindow.xaml.cs`
- `ModelPriorityEditor.cs`
- `OcrSettingsHelper.cs`
- `CliProxyUiOrchestrator.cs`

Model editor responsibilities:

- Endpoint preset selection.
- API key show/hide.
- Fetch Models.
- Model capability selection.
- Reasoning effort dropdown/custom value.

When adding settings:

1. Add XAML control.
2. Load it in `LoadConfig()`.
3. Save it in `BuildConfigFromInputs()`.
4. Add visibility/update helper if provider-specific.
5. Add config normalization/validation tests.

## Hotkeys

Hotkey parsing/normalization is centralized in `HotkeyBindingHelper`.

Supported primary keys include letters, digits, F1-F24, navigation keys, and OEM punctuation keys such as `OemPlus`, `OemMinus`, `OemComma`, `OemPeriod`, `Oem1`-`Oem8`, `Oem102`, and `OemClear`.

Do not split hotkey parsing across UI and hook code. UI should use `TryBuildBindingFromKeyEvent`; config should use `NormalizeBindingName` and `Validate`.

## Logging

Use `ILogger`; do not write directly to `Console`.

NLog setup:

- File target: `logs/suterusu-yyMMdd-HHmmss.log`.
- Console target enabled only with `--debug`.
- Format: `yyyy-MM-dd HH:mm:ss [logger] [LEVEL]: message`.

Common logger names:

- `Suterusu.App`
- `Suterusu.Config`
- `Suterusu.AI`
- `Suterusu.OCR.*`
- `Suterusu.CDP`
- `Suterusu.CliProxy`
- `Suterusu.Settings`

## Native interop

Keep all Win32 imports in `Suterusu/Interop/NativeMethods.cs` unless there is a strong reason to isolate dynamic function delegates like OneOCR.

Rules:

- Use correct calling conventions.
- Prefer explicit UTF-8 handling for native string buffers where required.
- Free native handles and unmanaged buffers in `finally`.
- Never scatter `DllImport` declarations across random services.

## Tests

Test project is `Suterusu.Tests` with xUnit.

Important test areas:

- `AiClientTests`: dispatch, fallback, VLM, reasoning request serialization.
- `ConfigTests`: defaults, normalization, validation, migration behavior.
- `ReasoningMetadataTests`: reasoning level metadata extraction and detail fetch behavior.
- OCR client tests: PaddleX, Windows OCR, OneOCR runtime locator/client, VLM.
- CLIProxy tests: login args, HTTP client behavior, config behavior.

Always build with MSBuild before running `dotnet test --no-build`.

## Documentation

Current docs:

- `docs/ocr/README.md`: OCR modes overview.
- `docs/ocr/*.md`: one file per OCR provider.
- `docs/chat/reasoning_level.md`: reasoning effort settings.

Use `[skip ci]` in docs-only commit messages when user requests it.

## Git notes

Recent committed work includes OCR provider docs, reasoning level metadata/debug fixes, VLM Chat screenshot mode, OneOCR provider, PaddleX provider, CLIProxyAPI Gemini/direct login changes, CDP event script injection, and OCR prompt handling.

Untracked local files commonly present and intentionally not committed:

- `.memory/`
- `test_ocr.png`
