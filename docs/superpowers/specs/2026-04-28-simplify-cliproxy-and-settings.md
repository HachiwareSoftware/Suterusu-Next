# Simplify CLIProxy and SettingsWindow

## Goal

Reduce SettingsWindow.xaml.cs from ~1,238 lines to ~550 lines by extracting CLIProxy, OCR, and Model Priority editing into dedicated helper classes. Also break up `CliProxyProcessManager.LoginWithBrowserOAuthAsync` from a 136-line monolith into focused phase methods.

No behavioral changes. Everything must continue to work identically.

## Architecture

- **No new service-layer abstractions** — only UI-layer helper classes
- Each helper owns a set of XAML elements and a slice of the event handlers
- Helpers are instantiated in SettingsWindow constructor and assigned to fields
- Helpers communicate back via constructor-injected delegates: `Action<string> showValidation`, `Action hideValidation`, `Action<string> updateStatus`. No direct SettingsWindow reference needed.

## Files to create

| File | Purpose | Est. lines |
|---|---|---|
| `Suterusu/UI/CliProxyUiOrchestrator.cs` | All CLI proxy event handlers + error-handling helper | ~220 |
| `Suterusu/UI/ModelPriorityEditor.cs` | Model priority CRUD, fetch models, presets | ~120 |
| `Suterusu/UI/OcrSettingsHelper.cs` | Windows OCR language dropdown, status, validation | ~80 |

## Files to modify

| File | Change | Est. lines removed |
|---|---|---|
| `Suterusu/UI/SettingsWindow.xaml.cs` | Remove extracted code, keep field decls + wiring | -420 |
| `Suterusu/Services/CliProxyProcessManager.cs` | Split LoginWithBrowserOAuthAsync into phase methods | -70 (net, more code but clearer) |
| `Suterusu/Suterusu.csproj` | Add 3 new `<Compile Include>` entries | +3 |

## Detailed design

### 1. CliProxyUiOrchestrator

**Dependencies** (constructor injection):
- `CliProxyProcessManager`
- `ConfigManager`
- `INotificationService`
- `ClipboardAiController` (nullable)
- `ILogger`
- `Action<string> showValidation`
- `Action hideValidation`
- `Action<string> updateStatus`

**Public surface:**
- All event handlers (same signatures as current SettingsWindow): `OnConnectAndUse`, `OnStart`, `OnStop`, `OnTest`, `OnRefreshModels`, `OnCheckForUpdates`, `OnUpdateCliProxy`
- `SetBusy(bool)` — enables/disables all CLI proxy UI controls
- `UpdateStatus(string)` — sets TxtCliProxyVersion/status text
- `RefreshVersionStatus()` — async version check

**Internal helpers:**
- `RunSafe(Func<Task<CliProxyResult>>, string errorPrefix)` — wraps busy guard + error handling + validation display
- `IsBusy` guard flag

**What moves here:**
All 14 CLI proxy methods from SettingsWindow (~360 lines). SettingsWindow keeps:
- Field `CliProxyUiOrchestrator _cliProxyOrchestrator`
- Constructor assignment
- XAML event handler stubs that delegate to orchestrator

### 2. ModelPriorityEditor

**Dependencies** (constructor injection):
- XAML element references (ListBox, TextBox, ComboBox, PasswordBox, Button, StackPanel)
- `Action<string> showValidation`
- `Action hideValidation`
- `ILogger`

**Public surface:**
- `OnAddNew()`, `OnEdit(ModelEntry)`, `OnDelete()`, `OnMoveUp()`, `OnMoveDown()`
- `OnConfirm()`, `OnCancel()`
- `OnPresetChanged(string presetName)`
- `OnFetchModels()`
- `PopulateList(IReadOnlyList<ModelEntry>)`
- `IsEditing` property — so SettingsWindow knows if in edit mode during save

**Internal state:**
- `_entries` — current model priority list (copied from config)
- `_editingIndex` — which entry is being edited (-1 = adding, -2 = not editing)
- `_isApplyingPreset` / `_isSyncingPreset` — suppress callback loops

**What moves here:**
All model priority CRUD methods (~120 lines). SettingsWindow keeps:
- Field `ModelPriorityEditor _modelEditor`
- Constructor assignment
- Event handler delegation stubs

### 3. OcrSettingsHelper

**Dependencies** (constructor injection):
- XAML element references (ComboBox, TextBlock)
- `Action<string> showValidation`
- `Action<string> updateStatus`
- `ILogger`

**Public surface:**
- `PopulateOcrLanguageDropdown(string selectedTag)` — queries `WindowsOcrClient.GetAvailability`, populates combo
- `GetSelectedLanguageTag()` — returns current combo selection
- `GetCurrentStatusMessage()` — returns availability status string
- `IsConfiguredValid(AppConfig)` — returns validation error or null
- `RefreshAvailableLanguages()` — button handler

**Internal state:**
- `_availability` — `WindowsOcrAvailability` snapshot

**What moves here:**
All OCR-related methods (~90 lines). SettingsWindow keeps:
- Field `OcrSettingsHelper _ocrHelper`
- Constructor assignment
- Event handler delegation stubs

### 4. CliProxyProcessManager.LoginWithBrowserOAuthAsync split

Current monolithic flow (136 lines) broken into:

```csharp
public async Task<CliProxyResult> LoginWithBrowserOAuthAsync(AppConfig config, CancellationToken cancellationToken)
{
    // Phase 0: write config, start process (existing)
    var process = StartCliProxyProcess(config, args);
    
    // Phase 1: wait for OAuth URL
    string oauthUrl = await WaitForOAuthUrlAsync(process, OAuthUrlTimeoutMs, cancellationToken);
    
    // Phase 2: open browser
    LaunchBrowser(oauthUrl);
    
    // Phase 3: wait for process exit
    await WaitForProcessExitAsync(process, LoginWaitTimeoutMs, cancellationToken);
    
    return CliProxyResult.Ok();
}

private async Task<string> WaitForOAuthUrlAsync(Process process, int timeoutMs, CancellationToken ct) { ... }
private void LaunchBrowser(string url) { ... }
private async Task WaitForProcessExitAsync(Process process, int timeoutMs, CancellationToken ct) { ... }
private string GetOAuthErrorDetail(Process process) { ... }
```

## No behavior changes

- All XAML stays identical (no element rename, no layout change)
- No config schema changes
- No third-party dependency changes
- Test count expected: 138 (same as current, migration test still passes)
- No new tests needed (pure refactoring, no behavior change)
