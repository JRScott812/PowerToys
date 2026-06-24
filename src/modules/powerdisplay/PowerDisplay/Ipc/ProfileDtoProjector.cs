// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// [UNVERIFIED] Not compiled (no VS C++ toolchain); build+verify on dev box.

using System;
using System.Collections.Generic;
using System.Globalization;
using PowerDisplay.Contracts;
using PowerDisplay.Models;
using PowerDisplay.ViewModels;

namespace PowerDisplay.Ipc;

/// <summary>
/// Pure-function projector that converts the app's profile model and per-monitor apply-outcomes
/// into the flat Contracts DTOs used by the CLI IPC renderers. Mirrors the projection logic
/// of <c>ProfilesCommand.Run</c> and <c>ApplyProfileCommand.RunAsync</c> in the CLI project.
/// </summary>
/// <remarks>
/// [UNVERIFIED] Not compiled (no VS C++ toolchain); build+verify on dev box.
/// </remarks>
public static class ProfileDtoProjector
{
    // ─── profiles (list) ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds the result DTO for the <c>profiles</c> command.
    /// Projects each <see cref="PowerDisplayProfile"/> in <paramref name="profiles"/> to a
    /// <see cref="CliProfileInfo"/>, matching the output of <c>ProfilesCommand.Run</c> exactly:
    /// <list type="bullet">
    ///   <item><see cref="CliProfileInfo.Name"/> — profile name.</item>
    ///   <item><see cref="CliProfileInfo.MonitorCount"/> — <c>MonitorSettings.Count</c>.</item>
    ///   <item><see cref="CliProfileInfo.LastModified"/> — <see cref="PowerDisplayProfile.LastModified"/>
    ///         formatted as ISO 8601 round-trip ("o") with invariant culture.</item>
    /// </list>
    /// </summary>
    /// <param name="profiles">The loaded profiles collection (may be empty; must not be null).</param>
    public static CliProfileListResult BuildProfileListResult(PowerDisplayProfiles profiles)
    {
        if (profiles is null)
        {
            throw new ArgumentNullException(nameof(profiles));
        }

        var infos = new List<CliProfileInfo>(profiles.Profiles.Count);
        foreach (var profile in profiles.Profiles)
        {
            if (profile is null)
            {
                continue;
            }

            infos.Add(new CliProfileInfo
            {
                Name = profile.Name ?? string.Empty,
                MonitorCount = profile.MonitorSettings?.Count ?? 0,

                // Mirror ProfilesCommand.Run: profile.LastModified.ToString("o", CultureInfo.InvariantCulture)
                LastModified = profile.LastModified.ToString("o", CultureInfo.InvariantCulture),
            });
        }

        return new CliProfileListResult { Profiles = infos };
    }

    // ─── apply-profile ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the result DTO for the <c>apply-profile</c> command from pre-computed
    /// per-monitor apply outcomes, and computes the process exit code.
    /// <para>
    /// Exit-code precedence (highest wins):
    /// <c>HardwareFailure (5) &gt; OutOfRange (2) &gt; Ok (0)</c>.
    /// </para>
    /// <para>
    /// <c>unsupported</c> settings do not affect the exit code (mirrors
    /// <c>ApplyProfileCommand.Record</c>).
    /// </para>
    /// </summary>
    /// <param name="profileName">The profile that was applied.</param>
    /// <param name="outcomes">
    /// Per-monitor outcomes produced by
    /// <see cref="MainViewModel.ApplyProfileWithOutcomesAsync"/>.
    /// </param>
    /// <returns>
    /// A tuple of the DTO to serialize to the IPC caller and the integer exit code the IPC
    /// handler should relay back (mirrors the return value of <c>ApplyProfileCommand.RunAsync</c>).
    /// </returns>
    public static (CliApplyProfileResult Result, int ExitCode) BuildApplyProfileResult(
        string profileName,
        IReadOnlyList<ProfileApplyOutcome> outcomes)
    {
        if (outcomes is null)
        {
            throw new ArgumentNullException(nameof(outcomes));
        }

        var monitorOutcomes = new List<CliProfileMonitorOutcome>(outcomes.Count);
        var anyHardwareFailure = false;
        var anyOutOfRange = false;

        foreach (var outcome in outcomes)
        {
            if (!outcome.Connected)
            {
                // Not connected — report with empty changes list and no monitor ref fields
                // beyond the ID (no number/name available since the monitor is offline).
                monitorOutcomes.Add(new CliProfileMonitorOutcome
                {
                    Monitor = new CliMonitorRef { Id = outcome.MonitorId },
                    Connected = false,
                    Changes = [],
                });
                continue;
            }

            var changes = new List<CliProfileChange>(outcome.Changes.Count);
            foreach (var (setting, status) in outcome.Changes)
            {
                changes.Add(new CliProfileChange
                {
                    Setting = setting,
                    Status = status,
                });

                // Accumulate worst-outcome flags (mirrors ApplyProfileCommand.Record).
                if (status == CliProfileChange.StatusHardwareFailure)
                {
                    anyHardwareFailure = true;
                }
                else if (status == CliProfileChange.StatusOutOfRange)
                {
                    anyOutOfRange = true;
                }

                // "unsupported" does not set any failure flag — intentional.
            }

            monitorOutcomes.Add(new CliProfileMonitorOutcome
            {
                Monitor = new CliMonitorRef { Id = outcome.MonitorId },
                Connected = true,
                Changes = changes,
            });
        }

        // Worst-outcome precedence: HardwareFailure > OutOfRange > Ok.
        var exitCode = anyHardwareFailure ? CliExitCodes.HardwareFailure
            : anyOutOfRange ? CliExitCodes.OutOfRange
            : CliExitCodes.Ok;

        var result = new CliApplyProfileResult
        {
            Ok = exitCode == CliExitCodes.Ok,
            Profile = profileName ?? string.Empty,
            Monitors = monitorOutcomes,
        };

        return (result, exitCode);
    }
}
