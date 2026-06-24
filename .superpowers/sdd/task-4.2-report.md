# Task 4.2 Report: CLI IPC Dispatch + ProviderUnavailable

## Status
DONE_WITH_CONCERNS (unverified — no C++ toolchain in this environment)

## What Was Removed from Program.cs

- `using var monitorManager = new MonitorManager()` removed
- `CliSettingsReader.Read()` and `runtime.HiddenMonitorIds` removed
- `monitorManager.SetMaxCompatibilityMode(...)` removed
- The cancellation-race hard-exit block (`@194-210`): `Task.WhenAny(commandTask, cancellationWaiter)` + `Environment.Exit` removed; timeout/Ctrl+C still works via `OperationCanceledException` catch
- `using PowerDisplay.Cli.Settings` namespace import removed
- `using PowerDisplay.Common.Services` namespace import removed
- In-process command calls (`ListCommand.RunAsync(monitorManager, ...)`, etc.) replaced by IPC dispatch

**Kept:**
- `--timeout` option still bounds IPC connect/await (passed as `TimeSpan` to `IpcDispatcher`)
- Ctrl+C (`CancelKeyPress`) cancellation preserved
- Timer-based timeout with `timedOut` flag for message differentiation preserved

## New File: CliRequestBuilder.cs

`src/modules/powerdisplay/PowerDisplay.Cli/Ipc/CliRequestBuilder.cs`

Static factory methods, one per command:
- `BuildList()` → `CliRequestEnvelope { Command="list", List=new ListRequest() }`
- `BuildGet(monitorNumber, monitorId, settingFilter)` → `GetRequest`
- `BuildSet(SetCommandInputs)` → derives setting name + raw value from first non-null field via pattern matching; throws `InvalidOperationException` if no setting set (callers must call `CountSelectedSettings == 1` first)
- `BuildCapabilities(monitorNumber, monitorId)` → `CapabilitiesRequest`
- `BuildProfiles()` → `ProfilesRequest`
- `BuildApplyProfile(profileName)` → `ApplyProfileRequest`

## New File: IpcDispatcher.cs

`src/modules/powerdisplay/PowerDisplay.Cli/Ipc/IpcDispatcher.cs`

Core dispatch logic:
1. Serialize `CliRequestEnvelope` via `ContractsJsonContext.Default.CliRequestEnvelope`
2. Call `SendAsync` (injectable delegate)
3. If `null` → write `PROVIDER_UNAVAILABLE` error and return exit code 10
4. Otherwise disambiguate success vs. error (see below), render, return exit code

The `IpcDispatcher` accepts a `SendDelegate` (type alias for `Task<string?>(string, TimeSpan, CancellationToken)`) in its constructor to enable test injection. A convenience constructor creates a real `CliPipeClient` instance.

## Dispatch Flow in Program.cs

`DispatchAsync` (now `internal static`) performs pure-syntactic validation then delegates to the right `dispatcher.Send*Async` method. It is extracted as a static method to support tests that pass a stub dispatcher.

## Success/Error Disambiguation

**Approach:** First attempt to deserialize the response as `CliErrorResult`. If `errorResult.Ok == false` (the discriminator), treat it as an error. Otherwise deserialize as the command-specific success type.

**Rationale:** All success DTOs (`CliListResult`, `CliGetResult`, etc.) are initialized with `Ok = true`. `CliErrorResult` has `Ok` as a plain `bool` property with no default, so it deserializes to `false` unless the app explicitly sets it. The app sets it `false` for error envelopes. This means:
- Success JSON: `{"ok":true,...}` → `CliErrorResult.Ok` = true → skip error path → deserialize as success type
- Error JSON: `{"ok":false,"error":{...}}` → `CliErrorResult.Ok` = false → error path

**Edge case / concern:** If the app sends a success response that somehow contains `"ok":false`, the dispatcher would misclassify it. This is a contract invariant — all success types init `Ok = true` and the app must serialize them faithfully. This is documented in `IpcDispatcher.cs` comments.

**Alternative considered:** Trying the expected success type first and checking `result.Ok`. Rejected because it requires deserialization of a potentially large response just to re-deserialize it as `CliErrorResult` on error. Current approach does one small `CliErrorResult` parse first (cheap), then one success parse on the happy path.

## Syntactic Validation Kept CLI-Side

The following validations do NOT trigger an IPC round-trip:
1. **`set`: zero settings specified** → `ARGUMENT_ERROR` (7)
2. **`set`: more than one setting specified** → `ARGUMENT_ERROR` (7)
3. **`get --setting` unknown name** (not in `GetCommand.AllSettingNames`) → `ARGUMENT_ERROR` (7)
4. **`--timeout` negative** (already in `CliOptions` validator) → parse error → `ARGUMENT_ERROR` (7)

## "-n ignored" Warning (Carry-Forward)

When both `-n` and `-i` are supplied for `get`, `set`, and `capabilities`, the CLI emits `Resources.Warn_MonitorNumberIgnored(monitorNumber)` to stderr before the IPC call. This mirrors what the old in-process `MonitorResolver` did and what the app-side handler will do (app discards `-n` when `-i` is present).

## Localization TODO

`IpcDispatcher.cs` contains:
```csharp
// TODO(localization): map error Code -> localized message CLI-side instead of rendering
// app-provided Message. For now we render Message/Hint verbatim (byte-identical to English).
```
App-provided `Message`/`Hint` strings are rendered verbatim — no regression vs. prior behavior (same English text, different source).

## `--json` / `--max-compatibility`

Both options remain present in `CliOptions`. Removal deferred to Task 5.1. `--max-compatibility` is still parsed but its value is no longer passed to `SetMaxCompatibilityMode` (removed). The option now becomes a no-op on the CLI side; the app controls compatibility mode.

## Tests Written

`src/modules/powerdisplay/PowerDisplay.Cli.UnitTests/IpcDispatchTests.cs`

Tests coverage:
- All 6 commands with `null` stub response → exit code 10 (`ProviderUnavailable`)
- `list`, `get`, `set` with canned success JSON → exit code 0 + output rendered
- Error JSON response → correct exit code rendered to stderr
- `CliRequestBuilder` round-trip tests for `BuildSet` (brightness, power-state, no-setting throws), `BuildGet`, `BuildApplyProfile`

`CaptureOutput` implements `ICliOutput` with string capture for assertion.

## Uncertainties / Concerns

1. **`applyProfileResult.Ok = false` exit code**: The current `IpcDispatcher.SendApplyProfileAsync` maps `result.Ok ? CliExitCodes.Ok : CliExitCodes.HardwareFailure` when the success type deserializes. This is a guess — the app may carry a more precise exit code in the response envelope. If the app-side `CliApplyProfileResult` carries an `ExitCode` field (it currently doesn't), a minor update to IpcDispatcher would be needed.

2. **`CliErrorResult.Ok` disambiguation edge**: If a future app version sends `{"ok":true,...}` inside an error-shaped JSON (e.g., a schema change), the CLI would misclassify it as a success DTO. The wire contract must maintain `Ok=false` for all `CliErrorResult` emissions. This is an invariant, not a bug.

3. **`using PowerDisplay.Cli.Settings` still compiles in**: `CliSettingsReader` type and `CliRuntimeSettings` are still physically in the project (removed only their usage in `Program.cs`). The `using` for `PowerDisplay.Cli.Settings` has been removed from `Program.cs`. The Settings classes remain until Task 5.1 drops the Lib ProjectReference.

4. **`IpcDispatcher.SendApplyProfileAsync` exit code**: For partial failures, the app sends a `CliApplyProfileResult` with `Ok=false` (hardware failure or out-of-range). The dispatcher returns `CliExitCodes.HardwareFailure` for this case. If out-of-range partial failures should return `CliExitCodes.OutOfRange` (2), the app-side response would need to carry an explicit exit code. This was a simplification; verify with app-side team.

5. **All Command files still reference PowerDisplay.Common.Models/Services**: The old command files (`SetCommand.cs`, `GetCommand.cs`, etc.) still exist with their in-process implementations. They compile but are never called by the new `DispatchAsync`. Task 5.1 will remove these or the Lib ProjectReference. There is no code removal risk — dead code that still compiles.

## Fix (review findings, apply-profile exit code)

### Root Cause
`CliApplyProfileResult` had no `ExitCode` field, so the worst-outcome code computed in `ProfileDtoProjector.BuildApplyProfileResult` was discarded by `CliRequestHandler` (`var (applyResult, _exitCode) = ...`) and never serialized to the wire. `IpcDispatcher.SendApplyProfileAsync` then hardcoded `Ok==false → HardwareFailure(5)`, making OutOfRange(2) partial failures report exit 5 instead of 2.

### Contracts change (VERIFIED)
- Added `public int ExitCode { get; init; } = CliExitCodes.Ok;` to `PowerDisplay.Contracts/Results/CliApplyProfileResult.cs`.
- Extended `RoundTripTests.cs`:
  - Added `Assert.AreEqual(CliExitCodes.Ok, back.ExitCode)` to the existing `CliApplyProfileResult_round_trips_with_outcomes` test.
  - Added new `CliApplyProfileResult_ExitCode_survives_round_trip` test: sets `ExitCode = CliExitCodes.OutOfRange` (2), round-trips through JSON, asserts value survives.
- **Test result:** `Passed! - Failed: 0, Passed: 14, Skipped: 0, Total: 14` (`dotnet test ... -c Debug -p:Platform=x64 -r win-x64`)

### App projector fix [UNVERIFIED]
- `ProfileDtoProjector.BuildApplyProfileResult` (`PowerDisplay/Ipc/ProfileDtoProjector.cs`): added `ExitCode = exitCode` to the `CliApplyProfileResult` initializer. The tuple return signature is unchanged; the DTO now carries the exit code redundantly.

### App handler fix [UNVERIFIED]
- `CliRequestHandler.BuildResponseAsync` (`PowerDisplay/Ipc/CliRequestHandler.cs`, ~line 222): renamed `_exitCode` discard to `_` with a comment clarifying the exit code now travels in `applyResult.ExitCode`. `Serialize(applyResult)` serializes all fields including `ExitCode`.

### CLI dispatcher fix [UNVERIFIED]
- `IpcDispatcher.SendApplyProfileAsync` (`PowerDisplay.Cli/Ipc/IpcDispatcher.cs`): replaced `result.Ok ? CliExitCodes.Ok : CliExitCodes.HardwareFailure` with `result.ExitCode`. The DTO-carried exit code is now returned directly.

### Minors [UNVERIFIED]
- Removed unused `TryDeserialize<T>` generic helper from `IpcDispatcher.cs` (was declared but never called; `TryDeserializeError` is a separate non-generic method).
- Added explicit `Ok = false` to `WriteProviderUnavailable`'s `CliErrorResult` construction for consistency with the contract invariant.
- Added three new tests to `IpcDispatchTests.cs`: `ApplyProfile_OutOfRange_partial_failure_exits_2` (the regression test, asserts exit 2 not 5), `ApplyProfile_HardwareFailure_exits_5`, `ApplyProfile_full_success_exits_0`. Marked `[UNVERIFIED]` in class-level remarks; cannot compile in this environment.

### Residual uncertainty
- App and CLI sides (`PowerDisplay`, `PowerDisplay.Cli`) were not compiled — no C++ interop toolchain available. The projector, handler, and dispatcher changes are syntactically correct by static inspection; build+verify on dev box before merging.

## Fix (final review: apply-profile disambiguation)

### Guard change

Old guard (line 121):
```csharp
if (errorCandidate is not null && !errorCandidate.Ok)
```

New guard:
```csharp
if (errorCandidate is not null && !errorCandidate.Ok
    && !string.IsNullOrEmpty(errorCandidate.Error?.Code))
```

### Null-safety

`CliErrorResult.Error` has initializer `= new()` so it is never null at runtime. The `?.Code` null-conditional is defensive for any future refactor that removes the initializer, and costs nothing.

When a `CliApplyProfileResult` (Ok=false, no error object) is deserialized as `CliErrorResult`, the JSON has no `"error"` key; the deserializer leaves `Error` at its C# default — `new CliError()` with `Code = string.Empty`. The `IsNullOrEmpty` check catches this and correctly skips the error branch.

### Per-command re-trace (all 6 commands correct after fix)

| Command | App response | `Error?.Code` after deser-as-CliErrorResult | Takes error branch? | Exit code source |
|---|---|---|---|---|
| list error | `CliErrorResult Ok=false, Code="MONITOR_NOT_FOUND"` | non-empty | YES | `errorCandidate.Error.ExitCode` |
| get error | `CliErrorResult Ok=false, Code="MONITOR_NOT_FOUND"` | non-empty | YES | `errorCandidate.Error.ExitCode` |
| set error (validation) | `CliErrorResult Ok=false, Code="ARGUMENT_ERROR"` | non-empty | YES | `errorCandidate.Error.ExitCode` |
| capabilities error | `CliErrorResult Ok=false, Code="MONITOR_NOT_FOUND"` | non-empty | YES | `errorCandidate.Error.ExitCode` |
| apply-profile failure (OutOfRange) | `CliApplyProfileResult Ok=false, ExitCode=2, no error.code` | empty string | NO (new) | `result.ExitCode` = 2 ✓ |
| apply-profile failure (HardwareFailure) | `CliApplyProfileResult Ok=false, ExitCode=5, no error.code` | empty string | NO (new) | `result.ExitCode` = 5 ✓ |

Any success (Ok=true) for any command: `!errorCandidate.Ok` is false → skips error branch regardless of Code. ✓

### App-side error paths lacking Error.Code

None found. Every genuine error response in `CliRequestHandler.cs` and `ProfileDtoProjector.cs` constructs `CliErrorResult` with an explicit `CliErrorCodes.*` constant in `Error.Code`. The `ProviderUnavailable` synthetic error constructed in `IpcDispatcher.WriteProviderUnavailable` also sets `Code = CliErrorCodes.ProviderUnavailable`. No error path leaves `Error.Code` empty.

### Test logical trace

- `ApplyProfile_OutOfRange_partial_failure_exits_2`: response is a `CliApplyProfileResult` serialized via `ContractsJsonContext.Default.CliApplyProfileResult` (Ok=false, ExitCode=2). Deserialized as `CliErrorResult` → `Error.Code = ""` → new guard is false → falls to success path → `Deserialize<CliApplyProfileResult>` → `result.ExitCode = 2` returned. `Assert.AreEqual(2, exit)` passes. `output.StdoutLines.Count == 1` (from `WriteApplyProfileResult`). ✓
- `ApplyProfile_HardwareFailure_exits_5`: same path, ExitCode=5. `Assert.AreEqual(5, exit)` passes. ✓
- `Error_response_renders_error_and_returns_its_exit_code`: response is a `CliErrorResult` with `Code="MONITOR_NOT_FOUND"`. New guard: `Code` is non-empty → error branch → `WriteError` → `StderrLines` populated. ✓
- All existing tests (Ok=true success responses): `!errorCandidate.Ok` is false → skip error branch regardless of empty Code. ✓
