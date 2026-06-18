// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;
using PowerDisplay.Common.Models;
using PowerDisplay.Common.Services;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.Commands;

public static class GetCommand
{
    /// <summary>
    /// Canonical setting names accepted by <c>--setting</c>. The same identifiers
    /// are used in <see cref="CliSettingValue.Setting"/> so JSON consumers can
    /// switch on them.
    /// </summary>
    public static readonly string[] AllSettingNames =
    [
        "brightness",
        "contrast",
        "volume",
        "color-temperature",
        "input-source",
        "power-state",
        "orientation",
    ];

    public static async Task<int> RunAsync(
        IMonitorManager monitorManager,
        IReadOnlySet<string> hiddenMonitorIds,
        int? monitorNumber,
        string? monitorId,
        string? settingFilter,
        ICliOutput output,
        CancellationToken cancellationToken)
    {
        var monitors = await monitorManager.DiscoverMonitorsAsync(cancellationToken);
        monitors = MonitorFiltering.ExcludeHidden(monitors, hiddenMonitorIds);

        // No selector → emit every discovered monitor with its settings. This is the
        // "show me everything" shape; scripts that want a single monitor pass -n/-i.
        if (!monitorNumber.HasValue && string.IsNullOrEmpty(monitorId))
        {
            return EmitAll(monitors, settingFilter, output);
        }

        var (monitor, exit) = MonitorFiltering.ResolveSelected(monitors, monitorNumber, monitorId, "get", output);
        if (monitor is null)
        {
            return exit;
        }

        var monitorRef = SetCommand.ToRef(monitor);

        var entry = BuildEntry(monitor, monitorRef, settingFilter, out var settingError);
        if (settingError is not null)
        {
            output.WriteError(new CliErrorResult { Command = "get", Monitor = monitorRef, Error = settingError });
            return settingError.ExitCode;
        }

        output.WriteGetResult(new CliGetResult { Monitors = [entry!] });
        return CliExitCodes.Ok;
    }

    public static int EmitAll(IReadOnlyList<Monitor> monitors, string? settingFilter, ICliOutput output)
    {
        // An unknown --setting is monitor-independent; validate it once up front and emit the
        // error without pinning it to whichever monitor happened to be enumerated first.
        if (TryGetUnknownSettingError(settingFilter, out var settingError))
        {
            output.WriteError(new CliErrorResult { Command = "get", Error = settingError! });
            return settingError!.ExitCode;
        }

        var entries = new List<CliGetMonitorEntry>(monitors.Count);
        foreach (var monitor in monitors)
        {
            var monitorRef = SetCommand.ToRef(monitor);
            entries.Add(BuildEntry(monitor, monitorRef, settingFilter, out _)!);
        }

        output.WriteGetResult(new CliGetResult { Monitors = entries });
        return CliExitCodes.Ok;
    }

    public static CliGetMonitorEntry? BuildEntry(
        Monitor monitor,
        CliMonitorRef monitorRef,
        string? settingFilter,
        out CliError? error)
    {
        if (TryGetUnknownSettingError(settingFilter, out error))
        {
            return null;
        }

        IEnumerable<string> settingNames = settingFilter is null
            ? AllSettingNames
            : new[] { settingFilter.ToLowerInvariant() };

        var results = new List<CliSettingValue>();
        foreach (var name in settingNames)
        {
            // settingFilter was validated above and every AllSettingNames entry is handled by
            // the switch, so BuildSettingValue never returns null here.
            results.Add(BuildSettingValue(monitor, name)!);
        }

        return new CliGetMonitorEntry
        {
            Monitor = monitorRef,
            Settings = results,
        };
    }

    /// <summary>
    /// Validates the optional <c>--setting</c> filter against <see cref="AllSettingNames"/>.
    /// Returns <c>true</c> with a populated, monitor-independent error when the filter names an
    /// unknown setting; the error echoes the user's original input verbatim rather than the
    /// lower-cased lookup key.
    /// </summary>
    private static bool TryGetUnknownSettingError(string? settingFilter, out CliError? error)
    {
        error = null;
        if (settingFilter is null || System.Array.IndexOf(AllSettingNames, settingFilter.ToLowerInvariant()) >= 0)
        {
            return false;
        }

        error = new CliError
        {
            Code = CliErrorCodes.ArgumentError,
            ExitCode = CliExitCodes.ArgumentError,
            Setting = settingFilter,
            Message = $"unknown setting '{settingFilter}'",
            Hint = $"valid settings: {string.Join(", ", AllSettingNames)}",
        };
        return true;
    }

    private static CliSettingValue? BuildSettingValue(Monitor monitor, string settingName) => settingName switch
    {
        "brightness" => Reading("brightness", monitor.SupportsBrightness, monitor.ReadValues.HasFlag(MonitorReadFlags.Brightness), monitor.CurrentBrightness, v => v + "%"),
        "contrast" => Reading("contrast", monitor.SupportsContrast, monitor.ReadValues.HasFlag(MonitorReadFlags.Contrast), monitor.CurrentContrast, v => v + "%"),
        "volume" => Reading("volume", monitor.SupportsVolume, monitor.ReadValues.HasFlag(MonitorReadFlags.Volume), monitor.CurrentVolume, v => v + "%"),
        "color-temperature" => Reading("color-temperature", monitor.SupportsColorTemperature, monitor.ReadValues.HasFlag(MonitorReadFlags.ColorTemperature), monitor.CurrentColorTemperature, v => SetCommand.FormatDiscrete(0x14, v)),
        "input-source" => Reading("input-source", monitor.SupportsInputSource, monitor.ReadValues.HasFlag(MonitorReadFlags.InputSource), monitor.CurrentInputSource, v => SetCommand.FormatDiscrete(0x60, v)),
        "power-state" => Reading("power-state", monitor.SupportsPowerState, monitor.ReadValues.HasFlag(MonitorReadFlags.PowerState), monitor.CurrentPowerState, v => SetCommand.FormatDiscrete(0xD6, v)),

        // raw is the orientation in degrees; the display is derived from the index, so the
        // formatter ignores its argument rather than treating degrees as an index.
        "orientation" => Reading("orientation", !string.IsNullOrEmpty(monitor.GdiDeviceName), monitor.ReadValues.HasFlag(MonitorReadFlags.Orientation), SetCommand.OrientationDegreesValue(monitor.Orientation), _ => SetCommand.OrientationDegrees(monitor.Orientation)),
        _ => null,
    };

    /// <summary>
    /// Projects one setting. The value is reported only when the monitor both supports it and
    /// discovery actually read it (<see cref="Monitor.ReadValues"/>) — mirroring how <c>set</c>
    /// reports its "before" value only when known, so a default/stale field (e.g. the 50% seed on
    /// <c>CurrentContrast</c>) is never passed off as a live reading. <paramref name="supported"/>
    /// is reported independently so a consumer can tell "monitor can't do this" (supported:false)
    /// from "couldn't read it" (supported:true, value omitted).
    /// </summary>
    private static CliSettingValue Reading(string name, bool supported, bool read, int raw, Func<int, string> format)
    {
        var known = supported && read;
        return new CliSettingValue
        {
            Setting = name,
            Supported = supported,
            Raw = known ? raw : null,
            Display = known ? format(raw) : null,
        };
    }
}
