// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;
using PowerDisplay.Cli.Resolution;
using PowerDisplay.Common.Models;
using PowerDisplay.Common.Services;
using PowerDisplay.Common.Utils;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.Commands;

public static class SetCommand
{
    public static async Task<int> RunAsync(
        IMonitorManager monitorManager,
        IReadOnlySet<string> hiddenMonitorIds,
        SetCommandInputs inputs,
        ICliOutput output,
        CancellationToken cancellationToken)
    {
        var selected = CountSelectedSettings(inputs);
        if (selected == 0)
        {
            output.WriteError(new CliErrorResult
            {
                Command = "set",
                Error = new CliError
                {
                    Code = CliErrorCodes.ArgumentError,
                    ExitCode = CliExitCodes.ArgumentError,
                    Message = "no setting specified; pass one of --brightness/--contrast/--volume/--color-temperature/--input-source/--power-state/--orientation",
                },
            });
            return CliExitCodes.ArgumentError;
        }

        if (selected > 1)
        {
            output.WriteError(new CliErrorResult
            {
                Command = "set",
                Error = new CliError
                {
                    Code = CliErrorCodes.ArgumentError,
                    ExitCode = CliExitCodes.ArgumentError,
                    Message = "only one setting may be applied per 'set' call",
                    Hint = "split into multiple invocations: one --<setting> per call",
                },
            });
            return CliExitCodes.ArgumentError;
        }

        var monitors = await monitorManager.DiscoverMonitorsAsync(cancellationToken);
        monitors = MonitorFiltering.ExcludeHidden(monitors, hiddenMonitorIds);

        var (monitor, exit) = MonitorFiltering.ResolveSelected(monitors, inputs.MonitorNumber, inputs.MonitorId, "set", output);
        if (monitor is null)
        {
            return exit;
        }

        var monitorRef = ToRef(monitor);

        if (inputs.Brightness is { } brightness)
        {
            return await ApplyContinuousAsync(
                monitorManager,
                monitor,
                monitorRef,
                "brightness",
                brightness,
                monitor.SupportsBrightness,
                monitor.CurrentBrightness,
                monitor.ReadValues.HasFlag(MonitorReadFlags.Brightness),
                "internal panels and external monitors via DDC/CI",
                (mm, id, v, ct) => mm.SetBrightnessAsync(id, v, ct),
                output,
                cancellationToken);
        }

        if (inputs.Contrast is { } contrast)
        {
            return await ApplyContinuousAsync(
                monitorManager,
                monitor,
                monitorRef,
                "contrast",
                contrast,
                monitor.SupportsContrast,
                monitor.CurrentContrast,
                monitor.ReadValues.HasFlag(MonitorReadFlags.Contrast),
                "internal panel exposes only brightness via WmiMonitorBrightness; DDC/CI capabilities are not available",
                (mm, id, v, ct) => mm.SetContrastAsync(id, v, ct),
                output,
                cancellationToken);
        }

        if (inputs.Volume is { } volume)
        {
            return await ApplyContinuousAsync(
                monitorManager,
                monitor,
                monitorRef,
                "volume",
                volume,
                monitor.SupportsVolume,
                monitor.CurrentVolume,
                monitor.ReadValues.HasFlag(MonitorReadFlags.Volume),
                "monitor's VCP capabilities did not advertise audio speaker volume (0x62)",
                (mm, id, v, ct) => mm.SetVolumeAsync(id, v, ct),
                output,
                cancellationToken);
        }

        if (inputs.ColorTemperature is { } colorTemp)
        {
            return await ApplyDiscreteAsync(
                monitorManager,
                monitor,
                monitorRef,
                "color-temperature",
                0x14,
                colorTemp,
                monitor.SupportsColorTemperature,
                monitor.CurrentColorTemperature,
                monitor.ReadValues.HasFlag(MonitorReadFlags.ColorTemperature),
                monitor.VcpCapabilitiesInfo?.GetSupportedValues(0x14),
                "monitor's VCP capabilities did not advertise color preset (0x14)",
                (mm, id, v, ct) => mm.SetColorTemperatureAsync(id, v, ct),
                output,
                cancellationToken);
        }

        if (inputs.InputSource is { } inputSource)
        {
            return await ApplyDiscreteAsync(
                monitorManager,
                monitor,
                monitorRef,
                "input-source",
                0x60,
                inputSource,
                monitor.SupportsInputSource,
                monitor.CurrentInputSource,
                monitor.ReadValues.HasFlag(MonitorReadFlags.InputSource),
                monitor.SupportedInputSources,
                "monitor's VCP capabilities did not advertise input source (0x60)",
                (mm, id, v, ct) => mm.SetInputSourceAsync(id, v, ct),
                output,
                cancellationToken);
        }

        if (inputs.PowerState is { } powerState)
        {
            return await ApplyDiscreteAsync(
                monitorManager,
                monitor,
                monitorRef,
                "power-state",
                0xD6,
                powerState,
                monitor.SupportsPowerState,
                monitor.CurrentPowerState,
                monitor.ReadValues.HasFlag(MonitorReadFlags.PowerState),
                monitor.SupportedPowerStates,
                "monitor's VCP capabilities did not advertise power mode (0xD6)",
                (mm, id, v, ct) => mm.SetPowerStateAsync(id, v, ct),
                output,
                cancellationToken,
                confirmIfDisplayBlanking: !inputs.ConfirmPowerOff,
                confirmationSetting: "power-state");
        }

        if (inputs.Orientation is { } orientation)
        {
            return await ApplyOrientationAsync(monitorManager, monitor, monitorRef, orientation, output, cancellationToken);
        }

        // Unreachable: CountSelectedSettings already vetted the inputs.
        return CliExitCodes.ArgumentError;
    }

    public static int CountSelectedSettings(SetCommandInputs inputs)
    {
        int count = 0;
        if (inputs.Brightness.HasValue)
        {
            count++;
        }

        if (inputs.Contrast.HasValue)
        {
            count++;
        }

        if (inputs.Volume.HasValue)
        {
            count++;
        }

        if (inputs.ColorTemperature is not null)
        {
            count++;
        }

        if (inputs.InputSource is not null)
        {
            count++;
        }

        if (inputs.PowerState is not null)
        {
            count++;
        }

        if (inputs.Orientation is not null)
        {
            count++;
        }

        return count;
    }

    internal static string FormatDiscrete(byte vcpCode, int value)
    {
        var name = VcpNames.GetValueName(vcpCode, value);
        return name is null
            ? $"0x{value:X2}"
            : $"{name} (0x{value:X2})";
    }

    // VCP 0xD6 states that leave a headless caller staring at a dark panel: 0x02 Standby and
    // 0x03 Suspend (DPMS sleep, wake-on-input) as well as 0x04 Off (DPM) and 0x05 Off (Hard).
    // All four are gated behind --confirm-power-off; only 0x01 On applies without confirmation.
    internal static bool IsDisplayBlanking(int powerState) => powerState is 0x02 or 0x03 or 0x04 or 0x05;

    internal static string OrientationDegrees(int index) => index switch
    {
        0 => "0°",
        1 => "90°",
        2 => "180°",
        3 => "270°",
        _ => $"index {index}",
    };

    internal static int OrientationDegreesValue(int index) => index switch
    {
        0 => 0,
        1 => 90,
        2 => 180,
        3 => 270,
        _ => index,
    };

    private static async Task<int> ApplyContinuousAsync(
        IMonitorManager monitorManager,
        Monitor monitor,
        CliMonitorRef monitorRef,
        string settingName,
        int requested,
        bool supportsCheck,
        int beforeValue,
        bool beforeKnown,
        string unsupportedReason,
        Func<IMonitorManager, string, int, CancellationToken, Task<MonitorOperationResult>> apply,
        ICliOutput output,
        CancellationToken cancellationToken)
    {
        if (!supportsCheck)
        {
            return WriteUnsupported(output, monitorRef, settingName, unsupportedReason);
        }

        var rangeError = ContinuousValueValidator.Validate(settingName, requested);
        if (rangeError is not null)
        {
            output.WriteError(new CliErrorResult { Command = "set", Monitor = monitorRef, Error = rangeError });
            return rangeError.ExitCode;
        }

        var op = await apply(monitorManager, monitor.Id, requested, cancellationToken);

        // A blocking write that overran --timeout (or Ctrl+C) cancels the token but cannot be
        // interrupted mid-call; surface it as TIMEOUT rather than reporting a false success.
        cancellationToken.ThrowIfCancellationRequested();

        if (!op.IsSuccess)
        {
            return WriteHardwareFailure(
                output, monitorRef, settingName, requested.ToString(CultureInfo.InvariantCulture), op.ErrorMessage, "Hardware write failed");
        }

        output.WriteSetResult(new CliSetResult
        {
            Monitor = monitorRef,
            Setting = settingName,
            BeforeRaw = beforeKnown ? beforeValue : null,
            AfterRaw = requested,
            BeforeDisplay = beforeKnown ? beforeValue + "%" : null,
            AfterDisplay = requested + "%",
        });
        return CliExitCodes.Ok;
    }

    private static async Task<int> ApplyDiscreteAsync(
        IMonitorManager monitorManager,
        Monitor monitor,
        CliMonitorRef monitorRef,
        string settingName,
        byte vcpCode,
        string raw,
        bool supportsCheck,
        int beforeValue,
        bool beforeKnown,
        IReadOnlyList<int>? supportedValues,
        string unsupportedReason,
        Func<IMonitorManager, string, int, CancellationToken, Task<MonitorOperationResult>> apply,
        ICliOutput output,
        CancellationToken cancellationToken,
        bool confirmIfDisplayBlanking = false,
        string? confirmationSetting = null)
    {
        if (!supportsCheck)
        {
            return WriteUnsupported(output, monitorRef, settingName, unsupportedReason);
        }

        // Resolve (and verify against the monitor's advertised set) BEFORE the power-off
        // confirmation gate, so an off value the monitor never advertises reports the real
        // INVALID_DISCRETE_VALUE (exit 3) instead of demanding --confirm-power-off for a value
        // that could never be applied.
        var resolved = DiscreteValueResolver.TryResolve(vcpCode, settingName, raw, supportedValues, out var valueError);
        if (resolved is null)
        {
            output.WriteError(new CliErrorResult { Command = "set", Monitor = monitorRef, Error = valueError! });
            return valueError!.ExitCode;
        }

        // Gate any state that blanks the panel on the already-resolved value, so we never
        // re-parse the raw input (the friendly-name lookup scans 0x00-0xFF) just to decide
        // whether to ask for confirmation.
        if (confirmIfDisplayBlanking && IsDisplayBlanking(resolved.Value))
        {
            output.WriteError(new CliErrorResult
            {
                Command = "set",
                Monitor = monitorRef,
                Error = new CliError
                {
                    Code = CliErrorCodes.ArgumentError,
                    ExitCode = CliExitCodes.ArgumentError,
                    Setting = confirmationSetting,
                    Requested = raw,
                    Message = $"refusing to power down or sleep Monitor {monitorRef.Number} ({monitorRef.Name}) without confirmation",
                    Hint = "re-run with --confirm-power-off to power the display off or put it to sleep",
                },
            });
            return CliExitCodes.ArgumentError;
        }

        var op = await apply(monitorManager, monitor.Id, resolved.Value, cancellationToken);

        // A blocking write that overran --timeout (or Ctrl+C) cancels the token but cannot be
        // interrupted mid-call; surface it as TIMEOUT rather than reporting a false success.
        cancellationToken.ThrowIfCancellationRequested();

        if (!op.IsSuccess)
        {
            return WriteHardwareFailure(output, monitorRef, settingName, raw, op.ErrorMessage, "Hardware write failed");
        }

        output.WriteSetResult(new CliSetResult
        {
            Monitor = monitorRef,
            Setting = settingName,
            BeforeRaw = beforeKnown ? beforeValue : null,
            AfterRaw = resolved.Value,
            BeforeDisplay = beforeKnown ? FormatDiscrete(vcpCode, beforeValue) : null,
            AfterDisplay = FormatDiscrete(vcpCode, resolved.Value),
        });
        return CliExitCodes.Ok;
    }

    private static async Task<int> ApplyOrientationAsync(
        IMonitorManager monitorManager,
        Monitor monitor,
        CliMonitorRef monitorRef,
        string raw,
        ICliOutput output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(monitor.GdiDeviceName))
        {
            output.WriteError(new CliErrorResult
            {
                Command = "set",
                Monitor = monitorRef,
                Error = new CliError
                {
                    Code = CliErrorCodes.UnsupportedFeature,
                    ExitCode = CliExitCodes.UnsupportedFeature,
                    Setting = OrientationResolver.SettingName,
                    Message = $"Monitor {monitorRef.Number} ({monitorRef.Name}) does not have a GDI device name and cannot be rotated via Windows display settings",
                },
            });
            return CliExitCodes.UnsupportedFeature;
        }

        var index = OrientationResolver.TryResolve(raw, out var error);
        if (index is null)
        {
            output.WriteError(new CliErrorResult { Command = "set", Monitor = monitorRef, Error = error! });
            return error!.ExitCode;
        }

        var beforeIndex = monitor.Orientation;
        var beforeKnown = monitor.ReadValues.HasFlag(MonitorReadFlags.Orientation);
        var op = await monitorManager.SetRotationAsync(monitor.Id, index.Value, cancellationToken);

        // A blocking rotation that overran --timeout (or Ctrl+C) cancels the token but cannot be
        // interrupted mid-call; surface it as TIMEOUT rather than reporting a false success.
        cancellationToken.ThrowIfCancellationRequested();

        if (!op.IsSuccess)
        {
            return WriteHardwareFailure(
                output, monitorRef, OrientationResolver.SettingName, raw, op.ErrorMessage, "ChangeDisplaySettingsEx failed");
        }

        output.WriteSetResult(new CliSetResult
        {
            Monitor = monitorRef,
            Setting = OrientationResolver.SettingName,
            BeforeRaw = beforeKnown ? OrientationDegreesValue(beforeIndex) : null,
            AfterRaw = OrientationDegreesValue(index.Value),
            BeforeDisplay = beforeKnown ? OrientationDegrees(beforeIndex) : null,
            AfterDisplay = OrientationDegrees(index.Value),
        });
        return CliExitCodes.Ok;
    }

    // UNSUPPORTED_FEATURE envelope shared by the continuous and discrete set paths.
    private static int WriteUnsupported(ICliOutput output, CliMonitorRef monitorRef, string settingName, string unsupportedReason)
    {
        output.WriteError(new CliErrorResult
        {
            Command = "set",
            Monitor = monitorRef,
            Error = new CliError
            {
                Code = CliErrorCodes.UnsupportedFeature,
                ExitCode = CliExitCodes.UnsupportedFeature,
                Setting = settingName,
                Message = $"Monitor {monitorRef.Number} ({monitorRef.Name}) does not support {settingName} adjustment",
                Hint = $"reason: {unsupportedReason}",
            },
        });
        return CliExitCodes.UnsupportedFeature;
    }

    // HARDWARE_FAILURE envelope shared by the continuous, discrete, and orientation set paths.
    private static int WriteHardwareFailure(
        ICliOutput output, CliMonitorRef monitorRef, string settingName, string requested, string? errorMessage, string fallback)
    {
        output.WriteError(new CliErrorResult
        {
            Command = "set",
            Monitor = monitorRef,
            Error = new CliError
            {
                Code = CliErrorCodes.HardwareFailure,
                ExitCode = CliExitCodes.HardwareFailure,
                Setting = settingName,
                Requested = requested,
                Message = errorMessage ?? fallback,
            },
        });
        return CliExitCodes.HardwareFailure;
    }

    // Shared Monitor -> CliMonitorRef projection (also called by GetCommand/CapabilitiesCommand,
    // mirroring the existing cross-command reuse of FormatDiscrete/OrientationDegrees).
    internal static CliMonitorRef ToRef(Monitor m) => new()
    {
        Number = m.MonitorNumber,
        Id = m.Id,
        Name = m.Name,
        Method = m.CommunicationMethod,
    };
}
