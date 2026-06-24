# Task 3.2 Report: CLI Pipe Server Startup Wiring

## Status
DONE_WITH_CONCERNS (unverified — no C++ toolchain on this machine)

---

## Changes Made

### 1. `MainWindow.xaml.cs`

**Added `using System.Threading;`** at line 5 (after `using System.Diagnostics.CodeAnalysis;`).

**Added field** (after `_isShowingWindow`):
```csharp
private CancellationTokenSource? _cliServerCts;
```

**Modified `OnViewModelInitializationCompleted`** (was lines 120–125, now expanded):
- After `_hasInitialized = true` and before `AdjustWindowSizeToContent()`:
  ```csharp
  if (_cliServerCts is null)
  {
      _cliServerCts = new CancellationTokenSource();
      var handler = new PowerDisplay.Ipc.CliRequestHandler(ViewModel, this.DispatcherQueue);
      new PowerDisplay.Ipc.CliPipeServer(handler).Start(_cliServerCts.Token);
      Logger.LogInfo("MainWindow: CLI pipe server started");
  }
  ```

**Modified `Dispose()`** — prepended CTS cleanup before existing disposals:
```csharp
_cliServerCts?.Cancel();
_cliServerCts?.Dispose();
_cliServerCts = null;
```

### 2. `App.xaml.cs`

**Modified `Shutdown()`** — added before `Environment.Exit(0)`:
```csharp
if (_mainWindow is MainWindow mw)
{
    mw.Dispose();
}
```

---

## Exact Insertion Points

| File | Location | What was added |
|------|----------|----------------|
| `MainWindow.xaml.cs` | Line 5 (using block) | `using System.Threading;` |
| `MainWindow.xaml.cs` | After `_isShowingWindow` field (~line 129) | `private CancellationTokenSource? _cliServerCts;` |
| `MainWindow.xaml.cs` | `OnViewModelInitializationCompleted` body, after `_hasInitialized = true` | Server start guard + CliRequestHandler + CliPipeServer.Start |
| `MainWindow.xaml.cs` | `Dispose()` — first 3 lines | CTS Cancel + Dispose + null |
| `App.xaml.cs` | `Shutdown()` — before `Environment.Exit(0)` | `mw.Dispose()` call |

---

## CliRequestHandler Constructor Used

```csharp
new PowerDisplay.Ipc.CliRequestHandler(ViewModel, this.DispatcherQueue)
```

- `ViewModel` — the `public MainViewModel ViewModel` property (throws `InvalidOperationException` if null, but at this point initialization just completed so it is guaranteed non-null).
- `this.DispatcherQueue` — the WinUI `DispatcherQueue` property on `WindowEx`/`Window`. Confirmed already used at lines 287 and 509 in the same file as `DispatcherQueue.TryEnqueue(...)`.

---

## Dispatcher Acquisition

`this.DispatcherQueue` on `MainWindow` (a `WindowEx` which inherits `Window`). This is the standard WinUI 3 way to get the UI-thread dispatcher associated with the window. Already used elsewhere in the same file — confirmed correct.

---

## Double-Start Guard

`if (_cliServerCts is null)` — ensures the server starts at most once even if `OnViewModelInitializationCompleted` were ever called a second time. Static analysis of `InitializationCompleted` in the ViewModel confirms it is raised exactly once (in `CompleteInitializationAsync`'s `finally` block, which sets `IsInitialized = true` first, preventing re-entry). The null-check is a defensive belt-and-suspenders guard.

---

## Shutdown Hook Chosen

**`Dispose()` on `MainWindow`** — called by:
1. **`App.Shutdown()`** — the managed teardown method invoked by the tray icon's "Exit" and by `PowerDisplayTerminateAppMessage` via named pipe. Added `mw.Dispose()` there.
2. **`App.Shutdown()`** also covers the `OnNamedPipeMessage` terminate path (calls `Shutdown()`).

**Known gaps (concerns):**

- `RegisterEvent(Constants.TerminatePowerDisplayEvent(), () => Environment.Exit(0), "Terminate")` calls `Environment.Exit(0)` directly, bypassing `Shutdown()` and `Dispose()`. The CTS cancel will NOT fire on this path.
- `RunnerHelper.WaitForPowerToysRunner(...)` callback also calls `Environment.Exit(0)` directly — same gap.
- `TrayIconService` exit callback also calls `() => Environment.Exit(0)` directly rather than `Shutdown()`.

These are pre-existing patterns in the codebase (the `Environment.Exit(0)` paths bypass all managed cleanup). The pipe server background task will be torn down by the OS process exit regardless, so there is no resource leak — the concern is only that `CliPipeServer.AcceptLoopAsync` won't receive a clean cancellation signal on those paths. This is acceptable for a background pipe server; the OS will close the kernel pipe handles.

The `Shutdown()` path (tray "Exit" and named-pipe terminate message) does get a clean cancellation.

---

## Existing Code Confirmed Untouched

- `_hasInitialized = true` — still first line of `OnViewModelInitializationCompleted`
- `AdjustWindowSizeToContent()` — still last line of `OnViewModelInitializationCompleted`
- All other event registrations, hotkey service, message hook, window config, tray icon — unchanged
- `OnWindowClosed` still just calls `HideWindow()` (intentional — closes are suppressed/hidden, not a teardown path)
- `Shutdown()` method structure (log, destroy tray, exit) — preserved; `Dispose()` call inserted before `Environment.Exit(0)`

---

## Tests

No unit tests added. This startup/shutdown wiring is not unit-testable without the running WinUI app. Verification requires the manual smoke test described in the task brief:

1. Build and run the app via Runner with PowerDisplay enabled.
2. Connect a test client to the named pipe `PowerDisplay_Cli_Session_{sid}`.
3. Send `{"version":"1.0","command":"list","list":{}}\n`.
4. Assert receipt of a valid one-line JSON containing `monitors`.
5. Verify two consecutive connections both succeed (server loops).
6. Exit the app; verify no hang.

---

## Uncertainties

1. **`Environment.Exit(0)` bypass paths**: Three shutdown paths skip `Dispose()` entirely (see above). Pipe server background thread terminates on process exit anyway — no leak, but no clean cancellation signal.
2. **`Dispose()` double-call risk**: `App.Shutdown()` now calls `mw.Dispose()`. If anything else in the app also calls `Dispose()` on the window before `Shutdown()` returns, the CTS null-check prevents double-cancel. The existing `_hotkeyService?.Dispose()` and `_messageHook?.Dispose()` calls are also guarded by the `?` operator. Low-risk.
3. **`ViewModel` property at server start time**: `ViewModel` throws `InvalidOperationException` if `_viewModel` is null. At the time `OnViewModelInitializationCompleted` fires, `_viewModel` was set in the constructor before `RegisterEventHandlers()` and would not be null. Safe.
