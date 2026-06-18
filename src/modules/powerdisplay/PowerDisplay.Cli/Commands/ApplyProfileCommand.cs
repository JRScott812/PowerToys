// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;
using PowerDisplay.Cli.Resolution;
using PowerDisplay.Common.Models;
using PowerDisplay.Common.Services;
using PowerDisplay.Models;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.Commands;

/// <summary>
/// Applies a saved profile's per-monitor settings (brightness, contrast, volume, color temperature)
/// to the connected monitors, mirroring the GUI's profile-apply path. Profiles never carry power-state,
/// so there is no display-blanking concern here. The per-monitor/per-setting outcome is reported on
/// stdout even on partial failure (so scripts get the full breakdown); the process exit code reflects
/// the worst outcome.
/// </summary>
public static class ApplyProfileCommand
{
    public static async Task<int> RunAsync(
        IMonitorManager monitorManager,
        IReadOnlySet<string> hiddenMonitorIds,
        PowerDisplayProfiles profiles,
        string profileName,
        ICliOutput output,
        CancellationToken cancellationToken)
    {
        var profile = profiles.GetProfile(profileName);
        if (profile is null || !profile.IsValid())
        {
            output.WriteError(new CliErrorResult
            {
                Command = "apply-profile",
                Error = new CliError
                {
                    Code = CliErrorCodes.ArgumentError,
                    ExitCode = CliExitCodes.ArgumentError,
                    Requested = profileName,
                    Message = $"profile '{profileName}' not found",
                    Hint = "run 'powerdisplay profiles' to see available profiles",
                },
            });
            return CliExitCodes.ArgumentError;
        }

        var monitors = await monitorManager.DiscoverMonitorsAsync(cancellationToken);
        monitors = MonitorFiltering.ExcludeHidden(monitors, hiddenMonitorIds);

        var byId = new Dictionary<string, Monitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in monitors)
        {
            byId[monitor.Id] = monitor;
        }

        var outcomes = new List<CliProfileMonitorOutcome>(profile.MonitorSettings.Count);
        var anyHardwareFailure = false;
        var anyOutOfRange = false;

        foreach (var setting in profile.MonitorSettings)
        {
            if (string.IsNullOrEmpty(setting.MonitorId) || !byId.TryGetValue(setting.MonitorId, out var monitor))
            {
                outcomes.Add(new CliProfileMonitorOutcome
                {
                    Monitor = new CliMonitorRef { Id = setting.MonitorId ?? string.Empty },
                    Connected = false,
                });
                continue;
            }

            var changes = new List<CliProfileChange>();

            if (setting.Brightness is { } brightness)
            {
                var change = await ApplyContinuousAsync(
                    monitorManager,
                    monitor,
                    "brightness",
                    brightness,
                    monitor.SupportsBrightness,
                    (mm, id, value, ct) => mm.SetBrightnessAsync(id, value, ct),
                    cancellationToken);
                Record(changes, change, ref anyHardwareFailure, ref anyOutOfRange);
            }

            if (setting.Contrast is { } contrast)
            {
                var change = await ApplyContinuousAsync(
                    monitorManager,
                    monitor,
                    "contrast",
                    contrast,
                    monitor.SupportsContrast,
                    (mm, id, value, ct) => mm.SetContrastAsync(id, value, ct),
                    cancellationToken);
                Record(changes, change, ref anyHardwareFailure, ref anyOutOfRange);
            }

            if (setting.Volume is { } volume)
            {
                var change = await ApplyContinuousAsync(
                    monitorManager,
                    monitor,
                    "volume",
                    volume,
                    monitor.SupportsVolume,
                    (mm, id, value, ct) => mm.SetVolumeAsync(id, value, ct),
                    cancellationToken);
                Record(changes, change, ref anyHardwareFailure, ref anyOutOfRange);
            }

            if (setting.ColorTemperatureVcp is { } colorTemperature)
            {
                var change = await ApplyColorTemperatureAsync(monitorManager, monitor, colorTemperature, cancellationToken);
                Record(changes, change, ref anyHardwareFailure, ref anyOutOfRange);
            }

            outcomes.Add(new CliProfileMonitorOutcome
            {
                Monitor = SetCommand.ToRef(monitor),
                Connected = true,
                Changes = changes,
            });
        }

        var exitCode = anyHardwareFailure ? CliExitCodes.HardwareFailure
            : anyOutOfRange ? CliExitCodes.OutOfRange
            : CliExitCodes.Ok;

        output.WriteApplyProfileResult(new CliApplyProfileResult
        {
            Ok = exitCode == CliExitCodes.Ok,
            Profile = profile.Name,
            Monitors = outcomes,
        });
        return exitCode;
    }

    private static void Record(List<CliProfileChange> changes, CliProfileChange change, ref bool anyHardwareFailure, ref bool anyOutOfRange)
    {
        changes.Add(change);
        if (change.Status == CliProfileChange.StatusHardwareFailure)
        {
            anyHardwareFailure = true;
        }
        else if (change.Status == CliProfileChange.StatusOutOfRange)
        {
            anyOutOfRange = true;
        }
    }

    private static async Task<CliProfileChange> ApplyContinuousAsync(
        IMonitorManager monitorManager,
        Monitor monitor,
        string settingName,
        int value,
        bool supported,
        Func<IMonitorManager, string, int, CancellationToken, Task<MonitorOperationResult>> apply,
        CancellationToken cancellationToken)
    {
        if (!supported)
        {
            return new CliProfileChange { Setting = settingName, Value = value, Status = CliProfileChange.StatusUnsupported };
        }

        if (ContinuousValueValidator.Validate(settingName, value) is not null)
        {
            return new CliProfileChange { Setting = settingName, Value = value, Status = CliProfileChange.StatusOutOfRange };
        }

        var op = await apply(monitorManager, monitor.Id, value, cancellationToken);

        // A blocking write that overran --timeout (or Ctrl+C) cancels the token but cannot be
        // interrupted mid-call; surface it as TIMEOUT rather than reporting a false success.
        cancellationToken.ThrowIfCancellationRequested();

        if (!op.IsSuccess)
        {
            return new CliProfileChange { Setting = settingName, Value = value, Status = CliProfileChange.StatusHardwareFailure, Error = op.ErrorMessage };
        }

        return new CliProfileChange { Setting = settingName, Value = value, Display = value + "%", Status = CliProfileChange.StatusApplied };
    }

    private static async Task<CliProfileChange> ApplyColorTemperatureAsync(
        IMonitorManager monitorManager,
        Monitor monitor,
        int value,
        CancellationToken cancellationToken)
    {
        if (!monitor.SupportsColorTemperature)
        {
            return new CliProfileChange { Setting = "color-temperature", Value = value, Status = CliProfileChange.StatusUnsupported };
        }

        // Color temperature is a raw VCP byte; reject a corrupt out-of-byte value rather than
        // writing a truncated value to the device (matches the set-command hex guard).
        if (value is < 0 or > 0xFF)
        {
            return new CliProfileChange { Setting = "color-temperature", Value = value, Status = CliProfileChange.StatusOutOfRange };
        }

        var op = await monitorManager.SetColorTemperatureAsync(monitor.Id, value, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!op.IsSuccess)
        {
            return new CliProfileChange { Setting = "color-temperature", Value = value, Status = CliProfileChange.StatusHardwareFailure, Error = op.ErrorMessage };
        }

        return new CliProfileChange
        {
            Setting = "color-temperature",
            Value = value,
            Display = SetCommand.FormatDiscrete(0x14, value),
            Status = CliProfileChange.StatusApplied,
        };
    }
}
