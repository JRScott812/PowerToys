// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using PowerDisplay.Contracts;
using PowerDisplay.Cli.Resolution;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.Commands;

internal static class MonitorFiltering
{
    /// <summary>
    /// Drops monitors the user hid in PowerDisplay settings, matching the GUI which
    /// removes the same ids from its managed list.
    /// </summary>
    public static IReadOnlyList<Monitor> ExcludeHidden(
        IReadOnlyList<Monitor> monitors,
        IReadOnlySet<string> hiddenMonitorIds)
    {
        if (hiddenMonitorIds.Count == 0)
        {
            return monitors;
        }

        var kept = new List<Monitor>(monitors.Count);
        foreach (var m in monitors)
        {
            if (!hiddenMonitorIds.Contains(m.Id))
            {
                kept.Add(m);
            }
        }

        return kept;
    }

    /// <summary>
    /// Resolves the target monitor from the already-discovered, hidden-filtered list using the CLI
    /// selector flags, emitting the selector warning and any error envelope. Returns the resolved
    /// monitor, or <c>null</c> plus the exit code to return when resolution fails. Callers pass the
    /// already-discovered list so discovery runs exactly once per invocation.
    /// </summary>
    public static (Monitor? Monitor, int ExitCode) ResolveSelected(
        IReadOnlyList<Monitor> monitors,
        int? monitorNumber,
        string? monitorId,
        string command,
        ICliOutput output)
    {
        var resolution = MonitorResolver.Resolve(monitors, monitorNumber, monitorId);

        if (resolution.Warning is not null)
        {
            output.WriteWarning(resolution.Warning);
        }

        if (resolution.Error is not null)
        {
            output.WriteError(new CliErrorResult { Command = command, Error = resolution.Error });
            return (null, resolution.Error.ExitCode);
        }

        return (resolution.Monitor, CliExitCodes.Ok);
    }
}
