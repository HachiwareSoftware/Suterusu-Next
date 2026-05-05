# AGENTS.md

Suterusu-Next repo brain. Caveman style. Substance exact.

## Project

Windows headless app. C# mostly. Main app .NET Framework 4.8. Clipboard ↔ AI, screenshot/OCR, VLM screenshot, CDP browser script injection, CLIProxyAPI helper.

Default hotkeys:

- `F6` clear chat history.
- `F7` send clipboard to AI.
- `F8` copy last AI response.
- `F12` quit.
- `Shift+F7` OCR/screenshot.

Run headless normally. `--open-settings` opens WPF settings. `--debug` enables console log. Config file beside exe: `config.json`.

## Hard Rules

- Windows-only.
- Main `Suterusu.csproj` old-style .NET Framework. Explicit compile includes. New `.cs` file? Add to csproj.
- Build main solution with VS MSBuild, not `dotnet build`.
- Tests use `dotnet test` after MSBuild.
- If build copy fails, running `Suterusu.exe` locks `bin/Debug/Suterusu.exe`. Stop process, rebuild.
- Leave `.memory/` and `test_ocr.png` untracked unless user says commit.
- No destructive git. No amend unless asked.

## Build/Test

Build:

```powershell
& "E:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" "Suterusu-Next.sln" /t:Build /p:Configuration=Debug /v:minimal
```

Test after build:

```powershell
dotnet test "Suterusu.Tests\Suterusu.Tests.csproj" --no-build --configuration Debug
```

Expected current tests: about `189` pass.

Release build same MSBuild, `Configuration=Release`.

## .NET 10 Helper

`Suterusu.WindowsAiOcr/` = Windows AI OCR helper. Targets `net10.0-windows10.0.26100.0`. VS 2022 MSBuild 17.x cannot build .NET 10 SDK. Helper built separate:

```powershell
& "$env:USERPROFILE\.dotnet10\dotnet.exe" build "Suterusu.WindowsAiOcr\Suterusu.WindowsAiOcr.csproj"
```

Output expected: `Suterusu/bin/Debug/WindowsAiOcr/Suterusu.WindowsAiOcr.exe`.

## Layout

- `Suterusu/` main app, old-style net48.
- `Suterusu.Tests/` xUnit net48.
- `Suterusu.WindowsAiOcr/` .NET 10 OCR helper.
- `docs/ocr/README.md` OCR docs index. GitHub previews this.
- `docs/ocr/*.md` one doc per OCR mode.
- `docs/chat/reasoning_level.md` reasoning effort docs.
- `.github/workflows/` CI/package.

## Startup/Lifetime

`Program.cs` parses args, configures NLog, opens settings or starts `HeadlessApplicationContext`.

`HeadlessApplicationContext` owns services. No IoC. Manual wiring. Add service = field + construct + refresh if config changes + dispose.

Core services:

- `ConfigManager` load/save/current config.
- `AiClient` chat dispatch.
- `ChatHistory` bounded history, vision history.
- `ClipboardAiController` hotkey actions, queue, OCR, VLM.
- `KeyboardHook` global keyboard hook.
- `NotificationServiceFactory` Flash/CircleDot/Nothing.
- `CdpService` CDP worker/injector.
- `CliProxyProcessManager` install/start/login/model flow.

## Config

Config class: `AppConfig` in `Suterusu/Configuration/AppConfig.cs`. JSON via `JsonSettings`, snake_case. Usually no `[JsonProperty]`.

Add config field:

1. Add property.
2. Add `CreateDefault()` default.
3. Add `Normalize()` trim/clamp/migrate.
4. Add `Validate()` only if runtime-breaking invalid.
5. Add `ConfigTests`.

Normalize after load. Save = validate → normalize → write.

## Chat Models

`ModelEntry` → `EndpointConfig`.

Fields matter:

- `Name`
- `BaseUrl`
- `ApiKey`
- `Model`
- `Capability`: `Auto`, `TextOnly`, `Vision`.
- `ReasoningEffort`: string, default `default`.

Capability only affects VLM screenshot. Normal clipboard chat ignores.

Reasoning:

- `default` omits `reasoning_effort`.
- Non-default sends exact `reasoning_effort` string.
- UI title-cases labels (`Default`, `Custom...`). Stored/request values remain raw/lowercase when raw is lowercase.
- Fetch Models uses explicit metadata only: direct fields or linked detail docs.
- Never invent levels from model name, provider name, endpoint URL, or `supported_parameters` alone.
- Debug logs in `ModelPriorityEditor`: fetch URL, auth present, HTTP status, data count, direct/detail levels, final dropdown options.

## AiClient

Modes:

- `Sequential`: try ordered entries.
- `RoundRobin`: start at `RoundRobinIndex`, advance on success.
- `Fastest`: race endpoints, first success wins.

`SendVisionAsync`: VLM screenshot path. Skips `TextOnly`. Tries `Vision` and `Auto`. Image-unsupported errors are recoverable fallback.

URL suffix handling lives in `AiClient`; avoid caller duplication.

## OCR

Providers enum:

- `LlamaCpp`
- `Zai`
- `Custom`
- `HuggingFace`
- `PaddleX`
- `OneOcr`
- `VlmChat`
- `WindowsOcr`
- `WindowsAi`

Factory: `OcrClientFactory.cs`.

Flow: `ClipboardAiController.ExecuteScreenOcrAsync()`:

1. Capture region PNG.
2. Optional downscale via `ImageResizer`.
3. OCR prompt or clipboard-as-prompt.
4. If `VlmChat`, call VLM flow.
5. Else call `IOcrClient`.
6. Store `LastAiResponse`, notify.

Prompt-aware: llama.cpp, Z.ai, Hugging Face, Custom, VLM Chat.

Prompt ignored: PaddleX, OneOCR, Windows OCR, Windows AI OCR.

Docs: `docs/ocr/README.md` + provider files.

### VLM Chat

`VlmChat` sends screenshot to configured chat vision models, not OCR.

- Chat `SystemPrompt` = system message.
- OCR prompt = image instruction text.
- History stores text only: `[Image] <prompt>`.
- No image bytes in history.
- Optional fallback OCR provider after all VLM models fail.

### OneOCR

Uses Snipping Tool OneOCR runtime via in-process C# P/Invoke.

Needs: `oneocr.dll`, `oneocr.onemodel`, `onnxruntime.dll`. 64-bit process.

Auto-detects Snipping Tool, copies files to app-local `OneOCR/` to bypass WindowsApps ACL.

Files: `OneOcrClient`, `OneOcrNative`, `OneOcrRuntimeLocator`.

### Windows OCR / Windows AI OCR

`WindowsOcr`: in-process `Windows.Media.Ocr`, installed OCR languages required.

`WindowsAi`: helper exe, Copilot+ PC/NPU intent, missing helper returns clear error.

### PaddleX

POST `{PaddleXUrl}/ocr`:

- `file`: base64 PNG bytes.
- `fileType`: `1`.
- `visualize`: `false`.

Parse `result.ocrResults[*].prunedResult.rec_texts` or `recTexts`.

## CLIProxyAPI

Dedicated settings tab. Config: `CliProxySettings`.

Providers:

- Codex/ChatGPT.
- Gemini.

Facts:

- `CliProxyProcessManager` installs/starts/logins.
- `BuildLoginArguments()` owns args.
- Gemini login uses direct visible CLIProxyAPI flow because `--login` needs stdin prompts.
- Codex can use no-browser path.
- Connect-and-use does not test guessed default model; Gemini models churn.
- Do not auto-insert or override `ModelPriority` entries.
- User adds CLIProxyAPI as normal chat model entry via preset.
- Preset URL: `http://127.0.0.1:8317/v1/chat/completions`.

## CDP

Config: `CdpSettings`.

Facts:

- Connect only to already-running CDP. Do not launch browser.
- Background thread: `Suterusu-CDP`.
- Logs to `logs/cdp-*.log` and main app log.
- Connects all injectable page targets, not first tab only.
- Skips DevTools/internal pages: `devtools://`, `chrome://`, `edge://`, `about:`.
- Script root default: `js/events`.
- Settings open creates `js/events/onconnect` and `js/events/onload`.
- Event folders: `onload`, `onconnect`, `onclick`, `onkeydown`, etc.
- Inject via `Page.addScriptToEvaluateOnNewDocument` + current-page eval.
- Disk script change hash triggers reload.
- Old persistent registration removed before replacement.
- Wrapper runs immediately with idempotency marker. No delayed event-listener wrapper.

## Settings UI

WPF code-behind, no MVVM.

Files:

- `SettingsWindow.xaml`
- `SettingsWindow.xaml.cs`
- `ModelPriorityEditor.cs`
- `OcrSettingsHelper.cs`
- `CliProxyUiOrchestrator.cs`

Model editor owns: presets, API key show/hide, Fetch Models, capability dropdown, reasoning dropdown/custom.

Add setting:

1. XAML control.
2. Load in `LoadConfig()`.
3. Save in `BuildConfigFromInputs()`.
4. Visibility helper if provider-specific.
5. Normalize/validate tests.

## Hotkeys

Central file: `HotkeyBindingHelper.cs`.

Supported keys include letters, digits, F1-F24, nav keys, OEM punctuation (`OemPlus`, `OemMinus`, `OemComma`, `OemPeriod`, `Oem1`-`Oem8`, `Oem102`, `OemClear`).

UI uses `TryBuildBindingFromKeyEvent`. Config uses `NormalizeBindingName` + `Validate`. Do not duplicate parser.

## Logging

Use `ILogger`. No `Console.WriteLine`.

NLog:

- File: `logs/suterusu-yyMMdd-HHmmss.log`.
- Console only with `--debug`.
- Format: timestamp, logger, level, message, exception.

Common names: `Suterusu.App`, `Suterusu.Config`, `Suterusu.AI`, `Suterusu.OCR.*`, `Suterusu.CDP`, `Suterusu.CliProxy`, `Suterusu.Settings`.

## Native Interop

Win32 imports live in `Suterusu/Interop/NativeMethods.cs`.

Rules:

- Correct calling conventions.
- Explicit UTF-8/native string handling where needed.
- Free handles/buffers in `finally`.
- Do not scatter `DllImport`.
- OneOCR dynamic delegates allowed in `OneOcrNative` because exports loaded from runtime path.

## Tests

Project: `Suterusu.Tests`, xUnit.

Areas:

- `AiClientTests`: dispatch, VLM, reasoning request serialization.
- `ConfigTests`: defaults/normalize/validate/migration.
- `ReasoningMetadataTests`: reasoning metadata/detail extraction.
- OCR tests: PaddleX, Windows OCR, OneOCR, VLM.
- CLIProxy tests: login args, HTTP behavior, config.

Always MSBuild first, then `dotnet test --no-build`.

## Docs

- `docs/ocr/README.md`: OCR overview.
- `docs/ocr/*.md`: provider docs.
- `docs/chat/reasoning_level.md`: reasoning effort docs.

Docs-only commit with `[skip ci]` when user asks.

## Git

Recent work: OCR docs, reasoning metadata/debug fix, VLM Chat, OneOCR, PaddleX, CLIProxy Gemini/direct login, CDP event injection, OCR prompt handling.

Common untracked keep out:

- `.memory/`
- `test_ocr.png`
