# Task 2.2 Report: App-side projector for list/get/capabilities DTOs

## Status
DONE_WITH_CONCERNS (unverified — cannot compile; build+verify on dev box)

---

## Files Created / Modified

| File | Action |
|------|--------|
| `src/modules/powerdisplay/PowerDisplay/Ipc/MonitorDtoProjector.cs` | Created — main projector |
| `src/modules/powerdisplay/PowerDisplay/PowerDisplay.csproj` | Modified — added `<ProjectReference>` to `PowerDisplay.Contracts` |
| `src/modules/powerdisplay/PowerDisplay.Ipc.UnitTests/PowerDisplay.Ipc.UnitTests.csproj` | Created — test project |
| `src/modules/powerdisplay/PowerDisplay.Ipc.UnitTests/MonitorDtoProjectorTests.cs` | Created — unit tests |
| `PowerToys.slnx` | Modified — added test project under `/modules/PowerDisplay/Tests/` |

---

## Methods Ported and Source

| Projector method | CLI source |
|-----------------|-----------|
| `BuildListResult` | `ListCommand.RunAsync` + `ListCommand.BuildEntry` |
| `BuildGetResult` / `BuildGetResultWithWarning` | `GetCommand.RunAsync` + `GetCommand.EmitAll` + `GetCommand.BuildEntry` |
| `BuildCapabilitiesResult` | `CapabilitiesCommand.RunAsync` |
| `ExcludeHidden` (internal) | `MonitorFiltering.ExcludeHidden` |
| `ResolveMonitor` (internal) | `MonitorResolver.Resolve` + `MonitorFiltering.ResolveSelected` |
| `ToRef` (private) | `SetCommand.ToRef` |
| `BuildListEntry` (private) | `ListCommand.BuildEntry` |
| `BuildGetEntry` (private) | `GetCommand.BuildEntry` |
| `TryGetUnknownSettingError` (private) | `GetCommand.TryGetUnknownSettingError` |
| `BuildSettingValue` (private) | `GetCommand.BuildSettingValue` |
| `Reading` (private) | `GetCommand.Reading` |
| `FormatDiscrete` (internal) | `SetCommand.FormatDiscrete` |
| `OrientationDegrees` (internal) | `SetCommand.OrientationDegrees` |
| `OrientationDegreesValue` (internal) | `SetCommand.OrientationDegreesValue` |

---

## Exact DTO Property Names Used

All property names verified against the Contracts source files:

**CliListResult**: `Ok`, `Version`, `Command`, `Monitors`  
**CliListMonitor**: `Number`, `Id`, `Name`, `Method`, `SupportsBrightness`, `SupportsContrast`, `SupportsVolume`, `SupportsColorTemperature`, `SupportsInputSource`, `SupportsPowerState`, `SupportsOrientation`  
**CliGetResult**: `Ok`, `Version`, `Command`, `Monitors`  
**CliGetMonitorEntry**: `Monitor`, `Settings`  
**CliSettingValue**: `Setting`, `Supported`, `Raw`, `Display`  
**CliMonitorRef**: `Number`, `Id`, `Name`, `Method`  
**CliCapabilitiesResult**: `Ok`, `Version`, `Command`, `Monitor`, `CommunicationMethod`, `RawCapabilities`, `Model`, `MccsVersion`, `VcpCodes`  
**CliVcpCodeInfo**: `Code`, `Name`, `Continuous`, `DiscreteValues`  
**CliErrorResult**: `Ok`, `Version`, `Command`, `Error`, `Monitor`  
**CliError**: `Code`, `ExitCode`, `Message`, `Setting`, `Hint`

**Monitor model properties used**: `MonitorNumber`, `Id`, `Name`, `CommunicationMethod`, `GdiDeviceName`, `SupportsBrightness`, `SupportsContrast`, `SupportsVolume`, `SupportsColorTemperature`, `SupportsInputSource`, `SupportsPowerState`, `CurrentBrightness`, `CurrentContrast`, `CurrentVolume`, `CurrentColorTemperature`, `CurrentInputSource`, `CurrentPowerState`, `Orientation`, `ReadValues`, `VcpCapabilitiesInfo`, `CapabilitiesRaw`

---

## Faithfulness Notes

### Preserved exactly:
- Display string formats: `v + "%"` for brightness/contrast/volume; `FormatDiscrete(0x14/0x60/0xD6, v)` for discrete settings; `OrientationDegrees(index)` for orientation display; `"0xNN"` hex fallback
- Orientation raw-is-degrees: `Raw = OrientationDegreesValue(index)`, `Display = OrientationDegrees(index)` — the formatter ignores its `int` argument and re-reads `monitor.Orientation` directly, matching the CLI comment "formatter ignores its argument"
- `supported && read` gating: value is null when `!supported` OR `!read`; `Supported` field always reflects actual support regardless of read status
- Error codes and exit codes: `MonitorNotFound/1`, `SelectorMissing/6`, `ArgumentError/7` from `CliErrorCodes`/`CliExitCodes`
- Unknown setting echoes original user casing in `Error.Message` (verbatim, not `.ToLowerInvariant()`)
- Both-selectors: id wins, `-n` warning always present even when id not found
- `CapabilitiesResult.Monitor.Method` is null (omitted from JSON); method goes in top-level `CommunicationMethod`
- `GetResult` with no selector: unknown setting validated once, error has no Monitor ref (monitor-independent)
- `GetResult` with selector + unknown setting: error has Monitor ref set

### Design decisions made:
1. **Resource strings inlined**: `PowerDisplay.Cli.Properties.Resources` is `internal` to PowerDisplay.Cli and inaccessible from the app project. The projector inlines the three message templates needed (`SelectorMissing`, `MonitorNotFound`, `UnknownSetting`, `ValidSettings`, `Warn_MonitorNumberIgnored`, `Hint_RunList`) as `string.Format(CultureInfo.InvariantCulture, ...)` literals. These are machine-contract error messages, not prose, so non-localization is acceptable; the CLI itself uses the same invariant strings for JSON output.
2. **Warning surfacing**: The original `MonitorFiltering.ResolveSelected` wrote the warning to `ICliOutput.WriteWarning`. Since the projector is stateless (no ICliOutput), the warning is returned in the tuple. `BuildGetResult` discards it (callers who need it use `BuildGetResultWithWarning`). The app-side dispatcher (Task 2.3) will call `BuildGetResultWithWarning` and emit the warning appropriately.
3. **`BuildGetResult` two-variant API**: Added `BuildGetResultWithWarning` returning `(Result, Error, Warning)` triple; `BuildGetResult` is the simpler two-tuple form that discards the warning. Both are public; callers choose based on whether they need the warning.

---

## What Could Not Be Verified

1. **Compilation** — cannot build due to C++ `PowerToys.Interop` dependency.
2. **`PowerDisplay.csproj` AOT compatibility** — the app uses `PublishAot=true`. `MonitorDtoProjector` uses only simple generics and `Func<int,string>` delegates; should be AOT-safe, but cannot verify trimmer warnings without building.
3. **`PowerDisplay.Ipc.UnitTests` csproj** — references `PowerDisplay.csproj` (the full WinUI3 app), which requires the C++ toolchain. A lighter alternative would be to link only the projector .cs file directly; this is flagged as a concern for the dev-box build step.
4. **`internal` accessibility from test project** — `ResolveMonitor`, `ExcludeHidden`, `FormatDiscrete`, `OrientationDegrees`, `OrientationDegreesValue` are `internal`. The test project references `PowerDisplay.csproj`; an `[InternalsVisibleTo]` attribute may be needed on `PowerDisplay.csproj` if the test project assembly name doesn't match. The csproj has `<AssemblyName>PowerToys.PowerDisplay</AssemblyName>` so InternalsVisibleTo would need `PowerDisplay.Ipc.UnitTests` — this is not yet added.

---

## Concerns

1. **Missing `[InternalsVisibleTo]` in PowerDisplay.csproj** for the test project. Need to add `<InternalsVisibleTo Include="PowerDisplay.Ipc.UnitTests" />` to `PowerDisplay.csproj` (or make the helpers `public`) before tests can access them.
2. **Test project references full WinUI3 app**: The test project references `PowerDisplay.csproj`, which chains to `PowerToys.Interop.vcxproj`. This will fail in environments without the C++ toolchain. A future improvement: extract the projector into a separate `PowerDisplay.Ipc` library project that the test can reference without WinUI3 overhead.
3. **Resource string localization**: The inlined message strings are English-only. If the CLI's error messages are ever localized via Resources.resx, the projector will be out of sync. This is acceptable for M2 (JSON contract, not UI prose) but should be tracked.

---

## Tests Written

`PowerDisplay.Ipc.UnitTests/MonitorDtoProjectorTests.cs` — 32 test methods covering:
- `BuildListResult`: hidden exclusion, GDI orientation flag, field projection
- `BuildGetResult` (no selector): all monitors returned, unknown setting error, case-insensitive filter
- `BuildGetResult` (selected): by number, by id, hidden monitor → not-found, both selectors → id wins + warning
- `BuildGetResult` (setting values): brightness %, supported-but-unread → null, unsupported → null, orientation raw-is-degrees, case-insensitive filter
- `BuildCapabilitiesResult`: no selector → SelectorMissing, not found → MonitorNotFound, no VCP caps → empty, method placement, VCP code projection with FormatDiscrete
- `ResolveMonitor`: all resolution paths
- `FormatDiscrete` / `OrientationDegrees` / `OrientationDegreesValue`: formatting helpers
