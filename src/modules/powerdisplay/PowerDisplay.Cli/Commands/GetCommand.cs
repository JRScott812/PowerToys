// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;
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
        "brightness" => new CliSettingValue
        {
            Setting = "brightness",
            Raw = monitor.CurrentBrightness,
            Display = monitor.CurrentBrightness + "%",
            Supported = monitor.SupportsBrightness,
        },
        "contrast" => new CliSettingValue
        {
            Setting = "contrast",
            Raw = monitor.CurrentContrast,
            Display = monitor.CurrentContrast + "%",
            Supported = monitor.SupportsContrast,
        },
        "volume" => new CliSettingValue
        {
            Setting = "volume",
            Raw = monitor.CurrentVolume,
            Display = monitor.CurrentVolume + "%",
            Supported = monitor.SupportsVolume,
        },
        "color-temperature" => new CliSettingValue
        {
            Setting = "color-temperature",
            Raw = monitor.CurrentColorTemperature,
            Display = SetCommand.FormatDiscrete(0x14, monitor.CurrentColorTemperature),
            Supported = monitor.SupportsColorTemperature,
        },
        "input-source" => new CliSettingValue
        {
            Setting = "input-source",
            Raw = monitor.CurrentInputSource,
            Display = SetCommand.FormatDiscrete(0x60, monitor.CurrentInputSource),
            Supported = monitor.SupportsInputSource,
        },
        "power-state" => new CliSettingValue
        {
            Setting = "power-state",
            Raw = monitor.CurrentPowerState,
            Display = SetCommand.FormatDiscrete(0xD6, monitor.CurrentPowerState),
            Supported = monitor.SupportsPowerState,
        },
        "orientation" => new CliSettingValue
        {
            Setting = "orientation",
            Raw = SetCommand.OrientationDegreesValue(monitor.Orientation),
            Display = SetCommand.OrientationDegrees(monitor.Orientation),
            Supported = !string.IsNullOrEmpty(monitor.GdiDeviceName),
        },
        _ => null,
    };
}
