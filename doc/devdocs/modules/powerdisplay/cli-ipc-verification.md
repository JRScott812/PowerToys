# PowerDisplay CLI IPC Thin-Client — Dev-Box Verification Checklist

This checklist captures verification steps that require a Windows dev box with the full
Visual Studio C++ toolchain, a running PowerToys installation, and physical monitors.
They could not be executed in the drafting environment (no VS C++ interop chain).

---

## Background

Milestones 1–5 refactored `PowerDisplay.Cli` from a standalone discovery tool into an
IPC thin-client of the running PowerDisplay app.

**User-visible changes:**

- `--json` flag removed (output is text-only).
- `--max-compatibility` flag removed (that mode is now a persistent UI setting in the app).
- All reads return a cache snapshot from the app's last discovery cycle; values may be
  slightly stale if a third-party tool changed hardware state between app discoveries.
- If the PowerDisplay app is not running, every command exits **10 (ProviderUnavailable)**
  with the message: "PowerDisplay is not running. Enable it in PowerToys settings."

**Packaging:** `installer/PowerToysSetupVNext/generateAllFileComponents.ps1` (line ~167)
auto-discovers binaries in `…\Release\WinUI3Apps`, so `PowerToys.PowerDisplay.Cli.exe`
and `PowerToys.PowerDisplay.Contracts.dll` are included automatically — no
`PowerDisplay.wxs` edit is required.

---

## Checklist

### 1. Build

- [ ] Run `tools\build\build.cmd` (full Release build) from the repo root.
- [ ] Confirm `PowerToys.PowerDisplay.Cli.exe` lands in `x64\Release\WinUI3Apps\`.
- [ ] Confirm `PowerToys.PowerDisplay.Contracts.dll` lands in `x64\Release\WinUI3Apps\`.
- [ ] Run the installer generator and confirm both files appear in the
      `WinUI3ApplicationsFiles` WiX component group (no manual `.wxs` edit needed).

### 2. Unit Tests

- [ ] Run `PowerDisplay.Contracts` unit tests (should already be green from drafting).
- [ ] Run `PowerDisplay.Cli.UnitTests` — these exercise the `Program.DispatchAsync` path,
      `CliOptions` parsing, `IpcDispatcher` with a stub `SendDelegate`, and
      `CliPipeClient.SendAsync` ordering. Confirm all pass.
- [ ] Run `PowerDisplay.Ipc.UnitTests` — these exercise the app-side `IpcServer`,
      `CliRequestHandler`, and `SetCommandExecutor`. Confirm all pass.
  - **Known carry-forward nit:** `SetCommandExecutorTests` has a stub `IMonitorManager`
    whose `VcpCodeInfo` ctor arity must match the actual `CliVcpCodeInfo` record. Confirm
    no compile error and tests pass.
  - **Known carry-forward nit:** `CliPipeClientTests` — confirm `await serverTask` is
    sequenced after the client assertion so the test does not race.

### 3. End-to-End — App Running

Prerequisites: PowerToys installed (or dev build); PowerDisplay module enabled in
PowerToys Settings; at least one DDC/CI-capable external monitor connected.

- [ ] `powerdisplay list`
  - Exit code **0**.
  - Text output lists at least one monitor with number, stable id, name, and transport.
- [ ] `powerdisplay get -n 1`
  - Exit code **0**.
  - Text output shows current brightness/contrast/volume/input-source for monitor 1.
  - Values are a cache snapshot from the app's last discovery; they may differ slightly
    from OSD-reported values if hardware was changed externally.
- [ ] `powerdisplay set -n 1 --brightness 60`
  - Exit code **0**.
  - Monitor brightness changes to 60 (observable on screen or OSD).
- [ ] `powerdisplay capabilities -n 1`
  - Exit code **0**.
  - Text output lists VCP codes the monitor advertises.
- [ ] `powerdisplay profiles`
  - Exit code **0** (or **0** with "No profiles saved." if none exist).
- [ ] `powerdisplay apply-profile "<profile name>"` (if a profile exists)
  - Exit code **0**.
  - Monitor settings visually change to match the profile values.
- [ ] `powerdisplay get -n 1 --setting brightness`
  - Exit code **0**.
  - Text output shows only the brightness line.
- [ ] `powerdisplay set -n 1 --power-state Standby` (without `--confirm-power-off`)
  - Exit code **7** (ArgumentError / confirmation required).
- [ ] `powerdisplay set -n 1 --power-state Standby --confirm-power-off`
  - Exit code **0** (monitor enters standby). Verify wake-up restores state.
- [ ] `powerdisplay --version`
  - Prints version string; exit code **0**.
- [ ] `powerdisplay --help`
  - Prints help text; exit code **0**.
  - Confirm `--json` and `--max-compatibility` do NOT appear in the help output.

### 4. End-to-End — App Not Running

- [ ] Stop PowerDisplay (disable it in PowerToys Settings, or terminate the process).
- [ ] Run any command, e.g. `powerdisplay list`.
  - Exit code **10** (ProviderUnavailable).
  - Stderr message contains "PowerDisplay is not running".
- [ ] Confirm the pattern holds for `get`, `set`, `capabilities`, `profiles`, and
      `apply-profile` — all should return exit **10** when the app is stopped.

### 5. Cross-Privilege Connectivity

Scenario: PowerToys (and therefore the PowerDisplay IPC server) runs elevated; the CLI
is invoked from a non-elevated terminal. The named-pipe ACL must allow non-elevated
clients to connect.

- [ ] Start PowerToys elevated (Run as Administrator).
- [ ] Open a non-elevated terminal.
- [ ] Run `powerdisplay list` from the non-elevated terminal.
  - Exit code **0** and monitor list is printed (not exit 10 / access denied).
- [ ] Run `powerdisplay set -n 1 --brightness 70` from the non-elevated terminal.
  - Exit code **0** and brightness changes (the app executes the DDC/CI write elevated).

### 6. AOT / Self-Contained Publish

- [ ] Publish `PowerDisplay.Cli` as a self-contained AOT binary (or confirm the release
      build pipeline does so).
- [ ] Confirm `System.IO.Pipes.AccessControl.dll` resolves and is present alongside
      the published binary. (This assembly is required for named-pipe ACL operations and
      may not be trimmed in by default.)
- [ ] Smoke-test the published binary with `powerdisplay list` against a running app.

### 7. Packaging Confirmation

- [ ] Install the PowerToys MSIX/setup produced from the build.
- [ ] Confirm `PowerToys.PowerDisplay.Cli.exe` and `PowerToys.PowerDisplay.Contracts.dll`
      are present in the install directory under `WinUI3Apps\`.
- [ ] Confirm `powerdisplay list` works from a standard (non-dev) PowerShell terminal
      after installation (the binary must be discoverable or callers must use the full
      path under the install dir).

---

## Known Carry-Forward Items (from Milestones 1–5)

These items were flagged during drafting and require dev-box confirmation:

| Item | File | Detail |
|------|------|--------|
| `VcpCodeInfo` ctor arity | `PowerDisplay.Ipc.UnitTests\SetCommandExecutorTests.cs` | Stub `IMonitorManager` references `CliVcpCodeInfo`; confirm arity matches the Contracts record definition. |
| `apply-profile` / `get` DTO behaviors | `PowerDisplay.Ipc.UnitTests\CliRequestHandlerTests.cs` | Confirm the app-side handler returns the correct DTO shape for `apply-profile` (per-monitor outcomes) and `get` (snapshot values). |
| `CliPipeClientTests` ordering nit | `PowerDisplay.Cli.UnitTests\CliPipeClientTests.cs` | `await serverTask` must be after the client assertion to avoid a race; confirm test does not flap. |

---

## Exit Code Reference

| Code | Name | Meaning |
|------|------|---------|
| 0 | Ok | Command succeeded. |
| 1 | MonitorNotFound | No monitor matched the supplied `-n`/`-i` selector. |
| 2 | OutOfRange | A supplied value was outside the monitor's valid range. |
| 3 | InvalidDiscreteValue | A discrete setting value (input-source, color-temp) was not recognized. |
| 4 | UnsupportedFeature | The monitor does not support the requested feature. |
| 5 | HardwareFailure | The DDC/CI or WMI write was rejected by the hardware. |
| 6 | SelectorMissing | The command requires `-n` or `-i` but neither was supplied. |
| 7 | ArgumentError | Command-line parse error or validation failure. |
| 8 | Timeout | The IPC round-trip did not complete within `--timeout` seconds. |
| 9 | InternalError | Unexpected exception in the CLI or a schema mismatch with the app. |
| **10** | **ProviderUnavailable** | **The PowerDisplay app is not running or the pipe could not be reached.** |
