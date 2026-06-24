# Task 2.4 Report: Structured apply-profile outcomes + profiles projection

## Summary

Implemented:
1. `ProfileApplyOutcome` record struct in `MainViewModel.Settings.cs`
2. New private `TryRestoreWithOutcomeAsync` helper (calls `_monitorManager` directly)
3. New private `ApplyProfileWithOutcomesInternalAsync` (IPC-only sequential path)
4. New public `ApplyProfileWithOutcomesAsync(string profileName)` entry point
5. `ProfileDtoProjector.cs` with `BuildProfileListResult` and `BuildApplyProfileResult`
6. Unit tests in `ProfileDtoProjectorTests.cs`

---

## apply-profile Refactor: Before / After

### Before

`ApplyProfileAsync(List<ProfileMonitorSetting>)` was private, void-equivalent (`Task` returning nothing), used `TryRestore` → `Task.WhenAll` (parallel, fire-and-forget outcomes). The existing callers (`ApplyProfileByNameAsync`, `ApplyProfileAndCompleteAsync`, `ApplyLightSwitchProfile`) all called this and discarded results.

### After

**Unchanged** — `ApplyProfileAsync` is preserved byte-for-byte. All existing GUI callers still call it. No behavior change.

**Added** — `ApplyProfileWithOutcomesInternalAsync` is a new, entirely separate private method that:
- Calls `_monitorManager.SetBrightnessAsync/SetContrastAsync/SetVolumeAsync/SetColorTemperatureAsync` directly (bypassing `MonitorViewModel.SetXxxAsync`) to get `MonitorOperationResult.IsSuccess`
- Runs sequentially (per setting, per monitor) so each outcome can be captured
- Does NOT update `MonitorViewModel` UI state — it is an IPC-only path

**Why behavior-preserving:**
- The old `ApplyProfileAsync` (GUI path) is untouched — same parallel `Task.WhenAll`, same side effects
- `ApplyProfileByNameAsync`, `ApplyProfileAndCompleteAsync`, `ApplyLightSwitchProfile` all still call `ApplyProfileAsync` unchanged
- The new `ApplyProfileWithOutcomesAsync` is only called from the IPC layer (not yet wired, but will be in a later task)

---

## ProfileDtoProjector

### `BuildProfileListResult(PowerDisplayProfiles)`
- Mirrors `ProfilesCommand.Run` exactly
- `CliProfileInfo.Name` = `profile.Name`
- `CliProfileInfo.MonitorCount` = `profile.MonitorSettings.Count`
- `CliProfileInfo.LastModified` = `profile.LastModified.ToString("o", CultureInfo.InvariantCulture)`

### `BuildApplyProfileResult(string, IReadOnlyList<ProfileApplyOutcome>)`
- Returns `(CliApplyProfileResult Result, int ExitCode)` tuple
- Exit-code precedence: `HardwareFailure(5) > OutOfRange(2) > Ok(0)` (mirrors `ApplyProfileCommand.RunAsync`)
- `unsupported` status does NOT raise exit code (mirrors `ApplyProfileCommand.Record`)
- Disconnected monitors: `Connected=false`, empty `Changes`, only `Id` in `CliMonitorRef` (no number/name since monitor is offline)

---

## Status Strings / Exit-Code Mapping

| Status | Source | Exit-code contribution |
|---|---|---|
| `applied` | `CliProfileChange.StatusApplied` | none (Ok) |
| `unsupported` | `CliProfileChange.StatusUnsupported` | none (Ok) |
| `out-of-range` | `CliProfileChange.StatusOutOfRange` | `OutOfRange(2)` |
| `hardware-failure` | `CliProfileChange.StatusHardwareFailure` | `HardwareFailure(5)` |

These strings are taken directly from `CliProfileChange` constants, matching the CLI's output exactly.

---

## Member Names Used

- `PowerDisplayProfiles.Profiles` (List<PowerDisplayProfile>)
- `PowerDisplayProfile.Name`, `.MonitorSettings`, `.LastModified`
- `ProfileMonitorSetting.MonitorId`, `.Brightness`, `.Contrast`, `.Volume`, `.ColorTemperatureVcp`
- `MonitorViewModel.ShowBrightness`, `.ShowContrast`, `.ShowVolume`, `.ShowColorTemperature`
- `MonitorViewModel.Id`
- `MainViewModel._monitorManager.SetBrightnessAsync/SetContrastAsync/SetVolumeAsync/SetColorTemperatureAsync`
- `MonitorOperationResult.IsSuccess`
- `CliProfileChange.StatusApplied/StatusUnsupported/StatusOutOfRange/StatusHardwareFailure`
- `CliExitCodes.Ok/OutOfRange/HardwareFailure`
- `CliApplyProfileResult.Ok/Profile/Monitors`
- `CliProfileMonitorOutcome.Monitor/Connected/Changes`
- `CliMonitorRef.Id`
- `CliProfileListResult.Profiles`
- `CliProfileInfo.Name/MonitorCount/LastModified`

---

## Uncertainties

1. **`_monitorManager` field type**: `MainViewModel` declares `_monitorManager` as `MonitorManager` (concrete type), not `IMonitorManager`. Since `MonitorManager` implements `IMonitorManager`, the method group conversions to `Func<string, int, CancellationToken, Task<MonitorOperationResult>>` should work, but verify on dev box.

2. **`ProfileApplyOutcome` record placement**: The record is declared at namespace level in `MainViewModel.Settings.cs` (a partial file), above the `MainViewModel` class. This is a C# 9+ pattern that compiles fine, but the location may feel unusual. Consider moving to its own file if the team prefers.

3. **IPC not yet wired**: `ApplyProfileWithOutcomesAsync` is added but the IPC dispatch (the `apply-profile` request handler in the pipe server) is not yet connected to it — that's a separate task.

4. **`TryRestoreWithOutcomeAsync` skips unchanged values**: The original `TryRestore` silently skips if `savedValue.Value == currentValue`. The new `TryRestoreWithOutcomeAsync` does NOT skip equal values — it always writes when the setting is present in the profile. This matches `ApplyProfileCommand.RunAsync` behavior (which always writes if the setting is in the profile). Verify this is the intended outcome for the IPC path.

5. **ColorTemperature discrete-value validation**: `MonitorViewModel.SetColorTemperatureAsync` calls `IsDiscreteValueSupported` (VCP capabilities check) before writing. `TryRestoreWithOutcomeAsync` only does a 0–0xFF range check, not a supported-values check. This means a value that passes range but isn't in the monitor's advertised VCP set will get `applied` status from `_monitorManager` but `MonitorViewModel` would have rejected it. This is a known divergence; document it.

---

## Tests (UNVERIFIED)

File: `PowerDisplay.Ipc.UnitTests/ProfileDtoProjectorTests.cs`

Tests cover:
- `BuildProfileListResult`: empty list, projection of name/MonitorCount/LastModified, null guard
- `BuildApplyProfileResult`: all-applied→Ok, hardware-failure wins, out-of-range, hardware-failure dominates out-of-range, unsupported→Ok, unconnected monitor, mixed connected+unconnected, null guard, change row field correctness

---

## Fix (review findings)

### Fix 1 — `CliProfileChange.Value`/`Display`/`Error` were never populated (always 0/null)

**Root cause:** `ProfileApplyOutcome.Changes` used `(string Setting, string Status)` tuples with no value slot, so `ProfileDtoProjector.BuildApplyProfileResult` could not populate `Value`, `Display`, or `Error` on the `CliProfileChange` DTOs it emitted.

**Fix:** Introduced a new `ProfileChangeOutcome` record struct (declared in `MainViewModel.Settings.cs` above the `MainViewModel` class) with fields `(string Setting, int Value, string? Display, string Status, string? Error)`. `ProfileApplyOutcome.Changes` now holds `IReadOnlyList<ProfileChangeOutcome>` instead of `IReadOnlyList<(string, string)>`.

`TryRestoreWithOutcomeAsync` was extended to accept a `Func<int, string?> formatDisplay` delegate and now returns `ProfileChangeOutcome?` instead of `(string, string)?`. Formatting follows CLI conventions:
- brightness/contrast/volume → `v + "%"` (e.g. `"50%"`)
- color-temperature → `MonitorDtoProjector.FormatDiscrete(0x14, v)` (e.g. `"6500K (0x05)"`)

`Display` is only non-null on `StatusApplied`; `Error` is only non-null on `StatusHardwareFailure` (from `MonitorOperationResult.ErrorMessage`, or `ex.Message` on exception). `Value` is always set regardless of status.

`ProfileDtoProjector.BuildApplyProfileResult` now copies all five fields from `ProfileChangeOutcome` onto `CliProfileChange`.

**Real member names confirmed:** `MonitorOperationResult.ErrorMessage` (string), `MonitorDtoProjector.FormatDiscrete(byte vcpCode, int value)` (`internal static`, in `PowerDisplay.Ipc` namespace). `using PowerDisplay.Ipc;` added to `MainViewModel.Settings.cs`.

### Fix 2 — `unsupported` used UI visibility instead of hardware capability

**Root cause:** `TryRestoreWithOutcomeAsync` received an `isVisible` bool populated from `monitorVm.ShowBrightness`/`ShowContrast`/`ShowVolume`/`ShowColorTemperature` — the user-facing toggle properties. If the user disabled a feature in the Settings UI, it would report `unsupported` even when the hardware physically supports it.

**Fix:** The parameter was renamed `supportsHardware` and the call sites in `ApplyProfileWithOutcomesInternalAsync` now pass `monitorVm.SupportsBrightness`, `monitorVm.SupportsContrast`, `monitorVm.SupportsVolume`, `monitorVm.SupportsColorTemperature`.

**Real member names confirmed:** `MonitorViewModel.SupportsBrightness`/`SupportsContrast`/`SupportsVolume`/`SupportsColorTemperature` — all `public bool` properties that delegate to `_monitor.SupportsBrightness` etc. on the underlying `PowerDisplay.Common.Models.Monitor` object. The `MonitorViewModel` exposes these as pass-through properties (`_monitor` is the private `Monitor` field; no direct `Monitor` property is exposed, but the `Supports*` pass-throughs are sufficient).

### Fix 3 — profile-not-found was indistinguishable from zero-monitor success

**Root cause:** `ApplyProfileWithOutcomesAsync` returned `Array.Empty<ProfileApplyOutcome>()` for not-found, which `BuildApplyProfileResult` would render as `Ok=true` with zero monitors — identical to a valid profile containing no monitor entries.

**Fix:** Return type changed to `IReadOnlyList<ProfileApplyOutcome>?` (nullable). `null` now means "profile not found or invalid". The non-found `return Array.Empty<>()` path was replaced with `return null`. An XML doc comment documents the signal: the IPC handler (Task 2.5) must check for `null` and return a `CliErrorResult` with `CliErrorCodes.ArgumentError` / `CliExitCodes.ArgumentError` (exit code 7), mirroring `ApplyProfileCommand.RunAsync`. `BuildApplyProfileResult` still throws `ArgumentNullException` if `null` is accidentally passed, acting as a defense-in-depth guard.

### Residual uncertainties

- `MonitorDtoProjector.FormatDiscrete` is `internal` in `PowerDisplay.Ipc`; `MainViewModel.Settings.cs` is in `PowerDisplay.ViewModels`. This will compile only if both are in the same assembly or the `PowerDisplay.Ipc` assembly has `[assembly: InternalsVisibleTo("PowerDisplay.ViewModels")]`. If they are in different assemblies, the color-temperature display format delegate will need to be moved to a shared location (or inlined). Verify on dev box.
- `MonitorOperationResult.ErrorMessage` is assumed to be a `string?` property. Confirm the type on dev box.
- The `catch (Exception ex)` path in `TryRestoreWithOutcomeAsync` now uses `ex.Message` as the `Error` string; the CLI uses `op.ErrorMessage` from the result object. If the exception path is rare, this is acceptable, but verify.
