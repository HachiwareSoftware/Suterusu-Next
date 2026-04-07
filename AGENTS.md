# AGENTS.md

Developer and agent reference for the Suterusu-Next repository.

---

## What this project is

Suterusu-Next is a headless Windows background utility written in **C# / .NET Framework 4.8**. It bridges the system clipboard and any OpenAI-compatible API. The user interacts entirely through global hotkeys:

| Key | Action |
|-----|--------|
| F6  | Clear chat history |
| F7  | Read clipboard, send to AI |
| F8  | Copy AI response to clipboard |
| F12 | Quit |

When an AI operation completes, the user is notified via a configurable mode: **FlashWindow** (taskbar flash), **CircleDot** (WinForms overlay), or **Nothing**. Configuration lives in `config.json` next to the executable. A WPF settings UI is available via the `--open-settings` CLI argument.

---

## Repository layout

```
Suterusu-Next.sln
│
├── Suterusu/                          Main application (WinExe, .NET 4.8, old-style csproj)
│   ├── Application/
│   │   ├── HeadlessApplicationContext.cs   Entry point after main(); owns all services + lifetime
│   │   └── SettingsWindowManager.cs        Creates / reuses the WPF SettingsWindow
│   ├── Bootstrap/
│   │   ├── ConsoleManager.cs               AllocConsole / FreeConsole for --debug
│   │   └── StartupOptions.cs               Parses --debug, --open-settings CLI flags
│   ├── Configuration/
│   │   ├── AppConfig.cs                    POCO config; CreateDefault(), Normalize(), Validate()
│   │   ├── ConfigManager.cs                Load / save config.json; backup on corruption
│   │   ├── JsonSettings.cs                 Newtonsoft.Json wrappers (snake_case, indented/compact)
│   │   └── NotificationMode.cs             Enum: FlashWindow | CircleDot | Nothing
│   ├── Hooks/
│   │   └── KeyboardHook.cs                 WH_KEYBOARD_LL low-level hook; fires HotkeyTriggered
│   ├── Interop/
│   │   └── NativeMethods.cs                All Win32 P/Invoke declarations (single source of truth)
│   ├── Models/                             Result types and request/response DTOs
│   ├── Notifications/
│   │   ├── INotificationService.cs         NotifySuccess() / NotifyFailure()
│   │   ├── NotificationServiceFactory.cs   Create(AppConfig) → correct implementation
│   │   ├── FlashWindowNotificationService.cs  EnumWindows-based taskbar flash
│   │   ├── CircleDotNotificationService.cs    WinForms STA overlay dot
│   │   └── NullNotificationService.cs         No-op
│   ├── Services/
│   │   ├── AiClient.cs                     HttpClient wrapper; per-model retry + fallback
│   │   ├── ChatHistory.cs                  Bounded conversation context; trim-to-limit
│   │   ├── ClipboardAiController.cs        Orchestrates F6/F7/F8; async queue processor
│   │   ├── ClipboardService.cs             Win32 clipboard read / write with retry
│   │   └── NLogLogger.cs                   NLog adapter (file always on, console in --debug)
│   └── UI/
│       ├── SettingsWindow.xaml             Dark-themed WPF settings UI
│       └── SettingsWindow.xaml.cs          Code-behind: load/save config, preset matching
│
└── Suterusu.Tests/                    xUnit test project (SDK-style csproj, net48)
    ├── ChatHistoryTests.cs
    ├── ConfigTests.cs
    ├── NotificationFactoryTests.cs
    └── ResultTypeTests.cs
```

---

## Build

The main project (`Suterusu.csproj`) is an **old-style ToolsVersion 15.0** project. It requires the **Visual Studio MSBuild**, not the standalone `dotnet` CLI. Using `dotnet build` will fail with unresolved NuGet packages.

```powershell
# Build (Debug)
& "E:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
    "Suterusu-Next.sln" /t:Build /p:Configuration=Debug /v:minimal

# Build (Release)
& "E:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
    "Suterusu-Next.sln" /t:Build /p:Configuration=Release /v:minimal

# Restore only
& "E:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
    "Suterusu-Next.sln" /t:Restore /v:minimal
```

Output: `Suterusu\bin\Debug\Suterusu.exe`

---

## Tests

The test project (`Suterusu.Tests`) is SDK-style and **can** use the `dotnet` CLI, but must be built first via MSBuild (see above) because it references the main project's output.

```powershell
# Run all tests (after MSBuild)
dotnet test "Suterusu.Tests\Suterusu.Tests.csproj" --no-build --configuration Debug
```

Expected: **82 tests, 0 failures**.

Always run the full test suite after any change. Do not skip tests.

---

## Architecture notes

### Service wiring

`HeadlessApplicationContext` constructs everything. There is no IoC container — dependencies are wired manually in the constructor. When adding a new service:

1. Add it as a field.
2. Instantiate it in the constructor.
3. Dispose it in `Dispose(bool)` if it holds resources.

### Notification pipeline

`NotificationServiceFactory.Create(AppConfig)` selects the implementation based on `AppConfig.NotificationMode`. The factory receives the full `AppConfig` so implementations can read their own settings (e.g. `FlashWindowTarget`, `FlashWindowDurationMs`). Never pass a bare `NotificationMode` enum to the factory.

### Win32 interop

All P/Invoke declarations live in `Suterusu/Interop/NativeMethods.cs`. Do not scatter `DllImport` across other files. Add new constants, structs, and imports there.

### Configuration

`AppConfig` is serialised to `config.json` using `JsonSettings.Serialize()`, which applies `SnakeCaseNamingStrategy` via Newtonsoft.Json. Property names are automatically converted:

- `ApiBaseUrl` → `api_base_url`
- `FlashWindowTarget` → `flash_window_target`
- `FlashWindowDurationMs` → `flash_window_duration_ms`

When adding a new config property:
1. Add the property to `AppConfig`.
2. Set a sensible default in `CreateDefault()`.
3. Add a guard / clamp in `Normalize()`.
4. No extra attributes or `ConfigManager` changes are needed.

### Result types

All service operations return typed result objects (e.g. `AiResponseResult`, `ClipboardReadResult`) rather than throwing or returning raw values. Follow this pattern for new operations.

---

## Key conventions

- **Logging**: use `ILogger` / `NLogLogger`, never `Console.WriteLine`. The constructor receives a logger name string (e.g. `"Suterusu.MyService"`).
- **Background threads**: `ClipboardAiController` dispatches work to `Task.Run`. Notification services are called from that background thread — sleeping in `NotifySuccess/Failure` is safe.
- **No MVVM**: the settings UI uses plain code-behind. Keep logic in the code-behind; do not introduce a ViewModel layer.
- **No IoC container**: keep manual wiring in `HeadlessApplicationContext`.
- **Snake_case JSON**: enforced globally by `JsonSettings`; no `[JsonProperty]` annotations needed.
- **Windows-only**: the project targets `net48` / `RuntimeIdentifiers=win`. Do not add cross-platform abstractions.
