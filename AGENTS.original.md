# AGENTS.md

Developer and agent reference for the Suterusu-Next repository.

---

## What this project is

Suterusu-Next is a headless Windows background utility written in **C# / .NET Framework 4.8**. It bridges the system clipboard and any OpenAI-compatible API. User interacts entirely through global hotkeys (configurable):

| Default Key | Action |
|-------------|--------|
| F6  | Clear chat history |
| F7  | Read clipboard, send to AI |
| F8  | Copy AI response to clipboard |
| F12 | Quit |

AI operation completes → user notified via configurable mode: **FlashWindow** (taskbar flash), **CircleDot** (WinForms overlay), or **Nothing**. Config in `config.json` next to executable. WPF settings UI via `--open-settings` CLI argument.

Supports multiple AI endpoints with three dispatch strategies: **Sequential**, **RoundRobin**, **Fastest**.

---

## Repository layout

```
Suterusu-Next.sln
│
├── Suterusu/                          Main app (WinExe, .NET 4.8, old-style ToolsVersion=15.0 csproj)
│   ├── Program.cs                     [STAThread] Main(); routes --open-settings vs headless path
│   ├── Application/
│   │   └── HeadlessApplicationContext.cs   ApplicationContext subclass; owns all services + lifetime
│   ├── Bootstrap/
│   │   ├── ConsoleManager.cs               AllocConsole / FreeConsole for --debug
│   │   └── StartupOptions.cs               Parses --debug, --open-settings CLI flags
│   ├── Configuration/
│   │   ├── AppConfig.cs                    POCO config; CreateDefault(), Normalize(), Validate()
│   │   ├── ConfigManager.cs                Load / save config.json; backup on corruption
│   │   ├── HotkeyBindingHelper.cs          Parse / normalize / validate hotkey binding strings
│   │   ├── JsonSettings.cs                 Newtonsoft.Json wrappers (snake_case, indented/compact)
│   │   ├── MultiRequestMode.cs             Enum: RoundRobin | Sequential | Fastest
│   │   └── NotificationMode.cs             Enum: FlashWindow | CircleDot | Nothing
│   ├── Hooks/
│   │   └── KeyboardHook.cs                 WH_KEYBOARD_LL low-level hook; fires HotkeyTriggered
│   ├── Interop/
│   │   ├── NativeMethods.cs                All Win32 P/Invoke declarations (single source of truth)
│   │   └── VirtualDesktopHelper.cs         COM IVirtualDesktopManager; moves overlay to current desktop
│   ├── Models/
│   │   ├── ChatCompletionRequest.cs        OpenAI API request DTO
│   │   ├── ChatCompletionResponse.cs       OpenAI API response DTO (Choice, ApiError)
│   │   ├── ChatMessage.cs                  {Role, Content} pair
│   │   ├── EndpointConfig.cs               {Name, BaseUrl, ApiKey, List<string> Models}
│   │   ├── EndpointPreset.cs               Hard-coded provider presets (OpenAI/Anthropic/OpenRouter/Ollama/llama.cpp/Custom)
│   │   ├── GlobalHotkey.cs                 Enum: ClearHistory | SendClipboard | CopyLastResponse | QuitApplication
│   │   ├── HotkeyBinding.cs                Value type: PrimaryKey + modifier booleans; ToDisplayString()
│   │   ├── ModelEntry.cs                   {Name, BaseUrl, ApiKey, Model}; ToEndpointConfig()
│   │   └── Results/
│   │       ├── AiResponseResult.cs         {Success, Content, ModelUsed, Error}
│   │       ├── AiSingleAttemptResult.cs    {Success, Content, Error}
│   │       ├── ClipboardReadResult.cs      {Success, Text, Error}
│   │       ├── ClipboardWriteResult.cs     {Success, Error}
│   │       ├── HotkeyActionResult.cs       {Success, Error}
│   │       ├── QueuedClipboardRequest.cs   Empty marker/sentinel for ConcurrentQueue
│   │       └── SaveConfigResult.cs         {Success, Error}
│   ├── Notifications/
│   │   ├── INotificationService.cs         NotifySuccess() / NotifyFailure()
│   │   ├── NotificationServiceFactory.cs   Create(AppConfig) → correct implementation
│   │   ├── FlashWindowNotificationService.cs  EnumWindows-based taskbar flash
│   │   ├── CircleDotNotificationService.cs    WinForms STA overlay dot with virtual desktop support
│   │   └── NullNotificationService.cs         No-op
│   ├── Services/
│   │   ├── AiClient.cs                     HttpClient wrapper; Sequential/RoundRobin/Fastest dispatch
│   │   ├── ChatHistory.cs                  Bounded conversation context; trim-to-limit
│   │   ├── ClipboardAiController.cs        Orchestrates F6/F7/F8; ConcurrentQueue + Interlocked processor
│   │   ├── ClipboardService.cs             Win32 clipboard read/write with retry (5×50ms)
│   │   ├── ILogger.cs                      Debug/Info/Warn/Error interface
│   │   └── NLogLogger.cs                   NLog adapter; file always on, console in --debug
│   └── UI/
│       ├── SettingsWindow.xaml             Dark-themed WPF settings UI (460×780)
│       └── SettingsWindow.xaml.cs          Code-behind: load/save config, preset matching, hotkey capture
│
├── Suterusu.Tests/                    xUnit test project (SDK-style csproj, net48)
│   ├── AiClientTests.cs               Timeout/fallback behavior (1 async test)
│   ├── ChatHistoryTests.cs            History trim, system prompt, Reset, UpdateConfiguration (20 tests)
│   ├── ConfigTests.cs                 CreateDefault, Normalize, Validate, hotkey parsing (~30 tests)
│   ├── NotificationFactoryTests.cs    Factory creates correct types, NullService no-throw (8 tests)
│   └── ResultTypeTests.cs             All 6 result types Ok/Fail invariants (32 tests)
│
└── plugins/                           Plugin artifacts (compiled only, no source in workspace)
    └── Suterusu.Plugins.Sample.CustomSelector/
        └── bin/{Debug,Release}/
            ├── manifest.json          Plugin metadata (id, type, entryPoint, dependencies)
            ├── Suterusu.Contract.dll  Plugin contract assembly
            └── Suterusu.Plugins.Sample.CustomSelector.dll
```

---

## Build

Main project (`Suterusu.csproj`) is **old-style ToolsVersion 15.0**. Requires **Visual Studio MSBuild** — `dotnet build` fails with unresolved NuGet packages.

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

NuGet packages:
- `Newtonsoft.Json 13.0.3`
- `NLog 6.1.2`

Framework refs: `System`, `System.Core`, `System.Net.Http`, `System.Windows.Forms`, `System.Drawing`, `PresentationCore`, `PresentationFramework`, `WindowsBase`, `System.Xaml`

---

## Tests

SDK-style test project; **can** use `dotnet` CLI but must be built via MSBuild first (references main project output).

```powershell
# Run all tests (after MSBuild build)
dotnet test "Suterusu.Tests\Suterusu.Tests.csproj" --no-build --configuration Debug
```

Expected: **82 tests, 0 failures**. Always run full suite after any change.

Test packages: `xunit 2.6.6`, `xunit.runner.visualstudio 2.5.6`, `Moq 4.20.72`, `Microsoft.NET.Test.Sdk 17.8.0`

---

## Entry point: Program.cs

`[STAThread] static void Main(string[] args)`:

1. `StartupOptions.Parse(args)`
2. `ConsoleManager.AllocDebugConsole()` or `FreeHeadlessConsole()`
3. `NLogLogger.Configure(options.DebugEnabled)` — one-time setup
4. If `--open-settings`: new `ConfigManager` → load config → new `System.Windows.Application` → `SettingsWindow.ShowDialog()` → return
5. Else: `Application.EnableVisualStyles()` → `new HeadlessApplicationContext(options)` → `Application.Run(context)`
6. Fatal exceptions → `MessageBox.Show` (debug mode only)

---

## Architecture notes

### Service wiring

`HeadlessApplicationContext` constructs everything. No IoC container — dependencies wired manually. Constructor wiring sequence:

1. `ConfigManager` (`"Suterusu.Config"`)
2. `_config = _configManager.LoadOrCreateDefault()`
3. `ClipboardService` (`"Suterusu.Clipboard"`)
4. `AiClient` (`"Suterusu.AI"`)
5. `NotificationServiceFactory.Create(_config)`
6. `ChatHistory(_config.SystemPrompt, _config.HistoryLimit)`
7. `ClipboardAiController` (all above services)
8. `KeyboardHook` (`"Suterusu.Hook"`)
9. `_keyboardHook.UpdateBindings(_config)`
10. `_keyboardHook.HotkeyTriggered += HandleHotkey`
11. `_keyboardHook.Install()`

`HandleHotkey` routes:
- `ClearHistory` → `_controller.ClearHistory()`
- `SendClipboard` → `_controller.EnqueueClipboardSend()`
- `CopyLastResponse` → `_controller.CopyLastResponseToClipboard()`
- `QuitApplication` → `ExitThread()`

`Dispose(bool)` disposes: `_keyboardHook`, `_controller`, `_aiClient`, `_notificationService` (if `IDisposable`).

When adding a new service: add field, instantiate in constructor, dispose in `Dispose(bool)`.

### Notification pipeline

`NotificationServiceFactory.Create(AppConfig)` switches on `config.NotificationMode`:
- `FlashWindow` → `new FlashWindowNotificationService(config.FlashWindowTarget, config.FlashWindowDurationMs)`
- `CircleDot` → `new CircleDotNotificationService(config.CircleDotBlinkCount, config.CircleDotBlinkDurationMs)`
- `Nothing` → `new NullNotificationService()`

Always pass full `AppConfig` to factory — never a bare `NotificationMode` enum.

### Win32 interop

All `DllImport` declarations in `Suterusu/Interop/NativeMethods.cs`. Do not scatter across other files.

Key declarations:
- `user32.dll`: `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`, `GetForegroundWindow`, `EnumWindows`, `IsWindowVisible`, `GetWindowThreadProcessId`, `FlashWindowEx`, `OpenClipboard`, `CloseClipboard`, `EmptyClipboard`, `GetClipboardData`, `SetClipboardData`, `IsClipboardFormatAvailable`, `SetWindowPos`, `GetWindowLong`, `SetWindowLong`, `ShowWindow`
- `kernel32.dll`: `GetModuleHandle`, `AllocConsole`, `FreeConsole`, `OpenProcess`, `CloseHandle`, `QueryFullProcessImageNameA`, `GlobalLock`, `GlobalUnlock`, `GlobalAlloc`, `GlobalSize`
- `ole32.dll`: `CoInitializeEx`, `CoUninitialize`, `CoCreateInstance`

Key constants: `WH_KEYBOARD_LL=13`, `CF_UNICODETEXT=13`, `GMEM_MOVEABLE=0x0002`, `FLASHW_TRAY=2`, `FLASHW_STOP=0`, `HWND_TOPMOST=-1`, `WS_EX_TOOLWINDOW=0x80`, `WS_EX_NOACTIVATE=0x08000000`, `COINIT_APARTMENTTHREADED=0x2`

`IVirtualDesktopManager` COM interface declared here (`[ComImport]`, GUID `A5CD92FF-29BE-454C-8D04-D82879FB3F1B`).

### Configuration

`AppConfig` serialised to `config.json` via `JsonSettings.Serialize()` → `SnakeCaseNamingStrategy` (Newtonsoft.Json). PascalCase auto-converts: `ApiBaseUrl` → `api_base_url`. No `[JsonProperty]` annotations needed.

**All AppConfig properties and defaults:**

| Property | Type | Default |
|---|---|---|
| `ModelPriority` | `List<ModelEntry>` | empty list |
| `SystemPrompt` | `string` | `"You are a helpful assistant."` |
| `HistoryLimit` | `int` | `10` |
| `NotificationMode` | `NotificationMode` | `FlashWindow` |
| `MultiRequestMode` | `MultiRequestMode` | `RoundRobin` |
| `MultiRequestTimeoutMs` | `int` | `60000` |
| `RoundRobinIndex` | `int` | `0` |
| `FlashWindowTarget` | `string` | `"Chrome"` |
| `FlashWindowDurationMs` | `int` | `1600` |
| `CircleDotBlinkCount` | `int` | `3` |
| `CircleDotBlinkDurationMs` | `int` | `600` |
| `ClearHistoryHotkey` | `string` | `"F6"` |
| `SendClipboardHotkey` | `string` | `"F7"` |
| `CopyLastResponseHotkey` | `string` | `"F8"` |
| `QuitApplicationHotkey` | `string` | `"F12"` |

**Normalize clamp rules:**
- `HistoryLimit`: 0–100
- `FlashWindowDurationMs`: 1–10000 (default 1600)
- `MultiRequestTimeoutMs`: 1–120000 (default 60000)
- `CircleDotBlinkCount`: 1–10 (default 3)
- `CircleDotBlinkDurationMs`: 200–5000
- `RoundRobinIndex`: ≥0
- `ModelPriority`: filter blank BaseUrl or Model
- Hotkeys: normalize via `HotkeyBindingHelper.NormalizeBindingName`; if any duplicates, all four reset to defaults

**Config pipeline:** `LoadOrCreateDefault` always calls `Normalize()`. `Save` calls `Validate()` → `Normalize()` → write JSON. Corruption → backup (`.bak.yyyyMMddHHmmss`), create default, save.

When adding a new config property:
1. Add to `AppConfig`
2. Set default in `CreateDefault()`
3. Add clamp in `Normalize()`
4. No other changes needed

### Hotkey binding system

**`HotkeyBindingHelper`** (`Suterusu/Configuration/HotkeyBindingHelper.cs`) — all hotkey parsing/normalization.

Binding strings format: `"Ctrl+Shift+K"`, `"F7"`, `"Alt+F4"`. Modifiers: CTRL/CONTROL, ALT, SHIFT, WIN/WINDOWS.

Supported primary keys: A–Z, D0–D9, F1–F24, Tab, Space, Return, Back, Insert, Delete, Home, End, PageUp, PageDown, Up, Down, Left, Right.

Key methods:
- `TryParseBinding(string, out HotkeyBinding)` — parse string → `HotkeyBinding`; false on duplicate tokens/unknown key
- `NormalizeBindingName(string, GlobalHotkey)` — parse + re-format canonical; fallback to default on failure
- `IsSupportedBindingName(string)` — delegates to `TryParseBinding`
- `GetDuplicateBindingErrors(string, string, string, string)` — detects duplicates across all four hotkeys
- `TryBuildBindingFromKeyEvent(Key, ModifierKeys, out string)` — WPF `Key` → binding string (for settings UI capture); rejects Escape and standalone modifiers
- `GetDefaultBinding(GlobalHotkey)` → `"F6"/"F7"/"F8"/"F12"`

**`HotkeyBinding`** (`Suterusu/Models/HotkeyBinding.cs`) — immutable value type:
- Properties: `Keys PrimaryKey`, `bool Control`, `bool Alt`, `bool Shift`, `bool Windows`
- `ToDisplayString()` → `"Ctrl+Alt+Shift+Win+KEY"`
- Full value equality (`Equals`, `GetHashCode`)

**`KeyboardHook`** (`Suterusu/Hooks/KeyboardHook.cs`):
- Installs `WH_KEYBOARD_LL` hook via `SetWindowsHookEx`
- `_bindings: Dictionary<Keys, List<RegisteredHotkey>>` — keyed by primary key
- `_pressedKeys: HashSet<Keys>` — repeat suppression; fires event once per keydown, clears on keyup
- `UpdateBindings(AppConfig)` rebuilds bindings dict and clears pressed state
- `HotkeyTriggered: EventHandler<GlobalHotkey>` — raised on message-pump thread
- `_proc` field kept alive to prevent GC of delegate

### Multi-request dispatch (AiClient)

`AiClient.SendAsync` dispatches based on `config.MultiRequestMode`:

- **Sequential**: try each `EndpointConfig` in order; return first success; collect all errors for failure
- **RoundRobin**: start at `config.RoundRobinIndex`, wrap around; on success advance index to `(i+1) % count`; mutates `config.RoundRobinIndex` in place
- **Fastest**: fire all endpoints concurrently with shared `CancellationTokenSource`; `Task.WhenAny` loop; first success cancels all others

`ModelEntry.ToEndpointConfig()` wraps single model into `EndpointConfig` with `Models = [Model]`.

Per-request timeout: `CancellationTokenSource.CancelAfter(config.MultiRequestTimeoutMs)`. URL construction: appends `/chat/completions` if URL doesn't already end with it.

`AiClient(ILogger logger, HttpMessageHandler handler)` constructor supports test injection.

### ClipboardAiController — async queue

`ClipboardAiController` fields:
- `ConcurrentQueue<QueuedClipboardRequest> _pendingRequests`
- `int _processorRunning` — Interlocked flag (0=idle, 1=running)
- `CancellationTokenSource _cts`
- `string LastAiResponse` — most recent AI response

`EnqueueClipboardSend()` → enqueue marker → `EnsureProcessorRunning()`:
- `Interlocked.CompareExchange` sets flag atomically; if not running → `Task.Run(ProcessQueueAsync)`
- `ProcessQueueAsync` drains queue serially; on empty: reset flag; if new items arrived during shutdown: re-call `EnsureProcessorRunning()`

`ExecuteClipboardSendAsync`: read clipboard → build messages → AI call → update history + `LastAiResponse` → `NotifySuccess()`; on any error → `NotifyFailure()`.

### CircleDot overlay — STA thread + virtual desktop

`CircleDotNotificationService` runs dedicated STA background thread (`"CircleDot-STA"`) with `Application.Run(form)` loop.

`OverlayForm` details:
- `FormBorderStyle.None`, `BackColor=Magenta`, `TransparencyKey=Magenta` (chroma key transparency)
- No taskbar icon, topmost
- `OnHandleCreated`: sets `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` via `GetWindowLong`/`SetWindowLong`; `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)`
- `OnPaint`: anti-aliased ellipse with `Color.FromArgb(_alpha, _dotColor)`
- Constants: `DotSize=14`, `MarginRight=20`, `MarginBottom=20`, `BlinkTimerMs=20`

`VirtualDesktopHelper.MoveWindowToCurrentDesktop(hwnd)` called after form shown — moves overlay to current virtual desktop via `IVirtualDesktopManager` COM interface. `TryInitializeComForCurrentThread` called on STA thread entry; `shouldUninitialize=true` only on `S_OK` (not `S_FALSE`/`RPC_E_CHANGED_MODE`).

Blink animation: two `Timer` objects — close timer (total duration) + blink timer (20ms interval, alpha oscillation).

`NotifySuccess()` → `Color.LimeGreen`; `NotifyFailure()` → `Color.Crimson`.

### FlashWindow notification

`FlashWindowNotificationService.FlashConfiguredWindow(count)`:
1. `EnumWindows` callback — skip invisible windows
2. Resolve exe name: `GetWindowThreadProcessId` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` → `QueryFullProcessImageNameA` (lowercased filename, no path)
3. `ShouldFlash(exeName, target)`: handles `"none"`/empty (false), `"all"` (true), exact/substring/with-extension match
4. `FlashHwnd`: `FlashWindowEx(FLASHW_TRAY, count)` → `Thread.Sleep(_durationMs)` → `FlashWindowEx(FLASHW_STOP)`
5. Stops after first match unless target is `"all"`

`NotifySuccess()` → 3 flashes; `NotifyFailure()` → 5 flashes.

Default target `"Chrome"` if blank; default duration `1600ms` if ≤0.

### Result types

All service operations return typed result objects — no exceptions thrown across service boundaries. Private constructors enforce via static factory methods.

| Type | Ok payload | Fail payload |
|---|---|---|
| `AiResponseResult` | `Content`, `ModelUsed` | `Error` |
| `AiSingleAttemptResult` | `Content` | `Error` |
| `ClipboardReadResult` | `Text` | `Error` |
| `ClipboardWriteResult` | — | `Error` |
| `HotkeyActionResult` | — | `Error` |
| `SaveConfigResult` | — | `Error` |

All: `bool Success` field. `Ok()` → Success=true, Error=null. `Fail(error)` → Success=false, payload=null.

### ChatHistory

- `HistoryLimit` counts turns (pairs), not messages. System message excluded from limit counting and from `Messages` property.
- `BuildRequestMessages(userText)` — under lock: [system msg if non-empty/non-null] + existing turns + new user msg → `AsReadOnly()`
- `AppendSuccessfulTurn(userText, assistantText)` — adds both, calls `TrimToLimit()`
- `TrimToLimit()` — removes oldest user+assistant pairs until `_turns.Count <= HistoryLimit * 2`
- `Reset(systemPrompt)` — clears turns, updates system prompt
- `UpdateConfiguration(systemPrompt, historyLimit)` — updates both, trims

### Logging

`NLogLogger`:
- Constructor: `public NLogLogger(string name)` — `LogManager.GetLogger(name)`
- `Configure(bool consoleEnabled)` — one-time thread-safe NLog setup
- File target: `logs/suterusu-yyMMdd-HHmmss.log` (timestamp fixed at startup, logs all Debug–Fatal)
- Optional `ColoredConsoleTarget` when `consoleEnabled`
- Format: `yyyy-MM-dd HH:mm:ss [logger] [LEVEL]: message{newline}{exception}`
- Never use `Console.WriteLine` — always use `ILogger`

Logger name conventions: `"Suterusu.App"`, `"Suterusu.Config"`, `"Suterusu.Clipboard"`, `"Suterusu.AI"`, `"Suterusu.Hook"`, `"Suterusu.Settings"`, `"Suterusu.Notification.Flash"`, `"Suterusu.Notification.CircleDot"`, `"Suterusu.Interop.VirtualDesktop"`

### Settings UI (SettingsWindow)

`SettingsWindow.xaml.cs` key fields:
- `Dictionary<GlobalHotkey, string> _hotkeyBindings` — in-memory during editing
- `int _editingEntryIndex` — `-2`=not editing, `-1`=adding new, `≥0`=editing existing
- `bool _isApplyingEntryPreset`, `bool _isSyncingEntryPreset` — suppress feedback loop in preset↔URL sync
- `GlobalHotkey? _capturingHotkey` — non-null when capturing key

Constructor args: `ConfigManager configManager, ClipboardAiController controller = null, Action<AppConfig> configSaved = null`. Controller optional (null when opened standalone via `--open-settings`).

Key methods:
- `LoadConfig(AppConfig)` — populate all controls from config; clones `ModelEntry` items
- `BuildConfigFromInputs()` → `AppConfig` from all controls
- `TrySave()` → `BuildConfigFromInputs` → `Validate` → `Save` → `controller?.RefreshConfiguration()` → `configSaved?.Invoke()`

Hotkey capture: `OnHotkeyButtonClick` → enter capture mode → `OnWindowPreviewKeyDown` → Escape cancels / valid non-modifier key calls `HotkeyBindingHelper.TryBuildBindingFromKeyEvent` → saves binding.

XAML colors: `WindowBrush=#1E1E1E`, `PanelBrush=#252526`, `AccentBrush=#0E639C`, `ErrorBrush=#A1260D`.

Layout sections: Model Priority (ListBox + CRUD + inline entry form) → Behavior (HistoryLimit, Hotkeys grid, Notification mode radios, MultiRequest mode radios, timeout, conditional Flash/CircleDot panels, System Prompt) → ValidationErrorBar (collapsed by default).

`EndpointPreset.GetPresets()` populates `CboEntryPreset`. Hard-coded presets:

| Name | BaseUrl | DefaultModel | RequiresApiKey |
|---|---|---|---|
| OpenAI | `https://api.openai.com/v1/chat/completions` | `gpt-5.4-mini` | true |
| Anthropic | `https://api.anthropic.com/v1/chat/completions` | `claude-3-5-sonnet-20241022` | true |
| OpenRouter | `https://openrouter.ai/api/v1/chat/completions` | `openai/gpt-5.4-mini` | true |
| Ollama | `http://localhost:11434/v1/chat/completions` | `llama3.2` | false |
| llama.cpp | `http://localhost:8080/v1/chat/completions` | `default` | false |
| Custom | _(empty)_ | _(empty)_ | true |

### Plugin system

`Suterusu.Contract.dll` defines plugin contract interfaces. Plugins loaded from `plugins/` subdirectories, each with `manifest.json`:

```json
{
  "id": "suterusu.plugins.sample.customselector",
  "name": "Custom Selector Plugin",
  "version": "1.0.0",
  "type": "model-selector",
  "entryPoint": "Suterusu.Plugins.Sample.CustomSelector.Plugin",
  "dependencies": ["Suterusu.Contract"]
}
```

Plugin type `"model-selector"` seen in sample. Plugin source not in workspace (compiled artifacts only in `plugins/*/bin/`).

---

## Cross-file dependency map

```
Program.cs
  ├── StartupOptions.Parse()
  ├── ConsoleManager
  ├── NLogLogger.Configure()
  ├── ConfigManager → SettingsWindow (--open-settings path)
  └── HeadlessApplicationContext
        ├── ConfigManager → AppConfig
        │     └── HotkeyBindingHelper (normalize/validate)
        │           └── HotkeyBinding
        ├── ClipboardService → NativeMethods (clipboard P/Invokes)
        ├── AiClient → HttpClient → ChatCompletionRequest/Response/ChatMessage
        ├── ChatHistory → ChatMessage
        ├── NotificationServiceFactory → AppConfig.NotificationMode
        │     ├── FlashWindowNotificationService → NativeMethods (EnumWindows, FlashWindowEx)
        │     ├── CircleDotNotificationService → WinForms STA + VirtualDesktopHelper
        │     │     └── VirtualDesktopHelper → NativeMethods (CoCreateInstance, IVirtualDesktopManager)
        │     └── NullNotificationService
        ├── ClipboardAiController
        │     ├── ClipboardService
        │     ├── AiClient
        │     ├── ChatHistory
        │     ├── ConfigManager (reads .Current for system prompt)
        │     └── INotificationService
        └── KeyboardHook → NativeMethods (WH_KEYBOARD_LL)
              └── HotkeyBindingHelper (parse bindings)

SettingsWindow (UI)
  ├── ConfigManager (load/save)
  ├── ClipboardAiController (optional RefreshConfiguration)
  ├── HotkeyBindingHelper (normalize, TryBuildBindingFromKeyEvent)
  ├── EndpointPreset (GetPresets for ComboBox)
  └── AppConfig (Validate, Normalize, BuildConfigFromInputs)
```

---

## Key conventions

- **Logging**: use `ILogger` / `NLogLogger`, never `Console.WriteLine`. Constructor receives logger name string (e.g. `"Suterusu.MyService"`).
- **Background threads**: `ClipboardAiController` dispatches to `Task.Run`. Notification services called from background thread — `Thread.Sleep` in `NotifySuccess/Failure` is safe.
- **STA for WinForms**: `CircleDotNotificationService` dedicates STA background thread for `Application.Run(form)` loop.
- **No MVVM**: settings UI uses plain code-behind. No ViewModel layer.
- **No IoC container**: manual wiring in `HeadlessApplicationContext`.
- **Snake_case JSON**: enforced globally by `JsonSettings`; no `[JsonProperty]` annotations needed.
- **Single P/Invoke source**: all `DllImport` in `NativeMethods.cs`.
- **Result types**: all service operations return typed result objects; private constructors + static factory methods.
- **Windows-only**: targets `net48` / `RuntimeIdentifiers=win`. No cross-platform abstractions.
- **Hotkey repeat suppression**: `HashSet<Keys> _pressedKeys` in `KeyboardHook` fires event once per physical press.
- **Config normalization pipeline**: always `Normalize()` after load; `Validate()` + `Normalize()` before save.
- **`SettingsWindowManager.cs` does not exist**: `Program.cs` instantiates `SettingsWindow` directly for `--open-settings`.
