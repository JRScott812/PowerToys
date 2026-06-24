// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// [UNVERIFIED] Not compiled (no VS C++ toolchain); build+verify on dev box.

using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Contracts;
using PowerDisplay.Ipc;
using PowerDisplay.Models;
using PowerDisplay.ViewModels;

namespace PowerDisplay.Ipc.UnitTests;

[TestClass]
public class ProfileDtoProjectorTests
{
    // ─── BuildProfileListResult ──────────────────────────────────────────────

    [TestMethod]
    public void BuildProfileListResult_EmptyProfiles_ReturnsEmptyList()
    {
        var profiles = new PowerDisplayProfiles();

        var result = ProfileDtoProjector.BuildProfileListResult(profiles);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Profiles.Count);
        Assert.AreEqual("profiles", result.Command);
    }

    [TestMethod]
    public void BuildProfileListResult_ProjectsNameMonitorCountAndLastModified()
    {
        var lastModified = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var profile = new PowerDisplayProfile
        {
            Name = "Night",
            MonitorSettings = new List<ProfileMonitorSetting>
            {
                new ProfileMonitorSetting("MON-A", brightness: 30),
                new ProfileMonitorSetting("MON-B", brightness: 40),
            },
            LastModified = lastModified,
        };

        var profiles = new PowerDisplayProfiles();
        profiles.Profiles.Add(profile);

        var result = ProfileDtoProjector.BuildProfileListResult(profiles);

        Assert.AreEqual(1, result.Profiles.Count);
        var info = result.Profiles[0];
        Assert.AreEqual("Night", info.Name);
        Assert.AreEqual(2, info.MonitorCount);
        // ISO 8601 round-trip ("o") format, invariant culture — mirrors ProfilesCommand.Run
        Assert.AreEqual(lastModified.ToString("o", System.Globalization.CultureInfo.InvariantCulture), info.LastModified);
    }

    [TestMethod]
    public void BuildProfileListResult_NullProfiles_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => ProfileDtoProjector.BuildProfileListResult(null!));
    }

    // ─── BuildApplyProfileResult — exit-code aggregation ─────────────────────

    [TestMethod]
    public void BuildApplyProfileResult_AllApplied_ExitCodeOk()
    {
        var outcomes = new List<ProfileApplyOutcome>
        {
            new("MON-A", Connected: true, Changes: new[]
            {
                ("brightness", CliProfileChange.StatusApplied),
                ("contrast",   CliProfileChange.StatusApplied),
            }),
        };

        var (result, exitCode) = ProfileDtoProjector.BuildApplyProfileResult("Day", outcomes);

        Assert.AreEqual(CliExitCodes.Ok, exitCode);
        Assert.IsTrue(result.Ok);
        Assert.AreEqual("Day", result.Profile);
    }

    [TestMethod]
    public void BuildApplyProfileResult_WorstOutcome_HardwareFailure_ExitCodeHardwareFailure()
    {
        // One monitor applied OK, another has a hardware failure → worst = HardwareFailure.
        var outcomes = new List<ProfileApplyOutcome>
        {
            new("MON-A", Connected: true, Changes: new[]
            {
                ("brightness", CliProfileChange.StatusApplied),
            }),
            new("MON-B", Connected: true, Changes: new[]
            {
                ("contrast", CliProfileChange.StatusHardwareFailure),
            }),
        };

        var (result, exitCode) = ProfileDtoProjector.BuildApplyProfileResult("Night", outcomes);

        Assert.AreEqual(CliExitCodes.HardwareFailure, exitCode);
        Assert.IsFalse(result.Ok);
    }

    [TestMethod]
    public void BuildApplyProfileResult_OutOfRange_ExitCodeOutOfRange()
    {
        var outcomes = new List<ProfileApplyOutcome>
        {
            new("MON-A", Connected: true, Changes: new[]
            {
                ("brightness", CliProfileChange.StatusApplied),
                ("volume",     CliProfileChange.StatusOutOfRange),
            }),
        };

        var (result, exitCode) = ProfileDtoProjector.BuildApplyProfileResult("Cinema", outcomes);

        Assert.AreEqual(CliExitCodes.OutOfRange, exitCode);
        Assert.IsFalse(result.Ok);
    }

    [TestMethod]
    public void BuildApplyProfileResult_HardwareFailureDominatesOutOfRange()
    {
        // HardwareFailure must win over OutOfRange regardless of order.
        var outcomes = new List<ProfileApplyOutcome>
        {
            new("MON-A", Connected: true, Changes: new[]
            {
                ("brightness", CliProfileChange.StatusOutOfRange),
                ("contrast",   CliProfileChange.StatusHardwareFailure),
            }),
        };

        var (_, exitCode) = ProfileDtoProjector.BuildApplyProfileResult("Profile", outcomes);

        Assert.AreEqual(CliExitCodes.HardwareFailure, exitCode);
    }

    [TestMethod]
    public void BuildApplyProfileResult_UnsupportedOnly_ExitCodeOk()
    {
        // "unsupported" does NOT contribute to exit-code failures (mirrors ApplyProfileCommand).
        var outcomes = new List<ProfileApplyOutcome>
        {
            new("MON-A", Connected: true, Changes: new[]
            {
                ("brightness", CliProfileChange.StatusUnsupported),
                ("contrast",   CliProfileChange.StatusUnsupported),
            }),
        };

        var (result, exitCode) = ProfileDtoProjector.BuildApplyProfileResult("Profile", outcomes);

        Assert.AreEqual(CliExitCodes.Ok, exitCode);
        Assert.IsTrue(result.Ok);
    }

    // ─── BuildApplyProfileResult — unconnected monitor ────────────────────────

    [TestMethod]
    public void BuildApplyProfileResult_UnconnectedMonitor_ConnectedFalseNoChanges()
    {
        var outcomes = new List<ProfileApplyOutcome>
        {
            new("MON-OFFLINE", Connected: false, Changes: Array.Empty<(string, string)>()),
        };

        var (result, exitCode) = ProfileDtoProjector.BuildApplyProfileResult("Profile", outcomes);

        Assert.AreEqual(CliExitCodes.Ok, exitCode);
        Assert.IsTrue(result.Ok);
        Assert.AreEqual(1, result.Monitors.Count);

        var mon = result.Monitors[0];
        Assert.IsFalse(mon.Connected);
        Assert.AreEqual("MON-OFFLINE", mon.Monitor.Id);
        Assert.AreEqual(0, mon.Changes.Count);
    }

    [TestMethod]
    public void BuildApplyProfileResult_MixedConnectedUnconnected_CorrectOutcomes()
    {
        var outcomes = new List<ProfileApplyOutcome>
        {
            new("MON-A",       Connected: true,  Changes: new[] { ("brightness", CliProfileChange.StatusApplied) }),
            new("MON-OFFLINE", Connected: false, Changes: Array.Empty<(string, string)>()),
        };

        var (result, exitCode) = ProfileDtoProjector.BuildApplyProfileResult("Profile", outcomes);

        Assert.AreEqual(CliExitCodes.Ok, exitCode);
        Assert.AreEqual(2, result.Monitors.Count);
        Assert.IsTrue(result.Monitors[0].Connected);
        Assert.IsFalse(result.Monitors[1].Connected);
    }

    [TestMethod]
    public void BuildApplyProfileResult_NullOutcomes_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(
            () => ProfileDtoProjector.BuildApplyProfileResult("Profile", null!));
    }

    // ─── BuildApplyProfileResult — DTO field correctness ────────────────────

    [TestMethod]
    public void BuildApplyProfileResult_ChangeRowsCarrySettingAndStatus()
    {
        var outcomes = new List<ProfileApplyOutcome>
        {
            new("MON-A", Connected: true, Changes: new[]
            {
                ("brightness",       CliProfileChange.StatusApplied),
                ("color-temperature", CliProfileChange.StatusUnsupported),
            }),
        };

        var (result, _) = ProfileDtoProjector.BuildApplyProfileResult("Profile", outcomes);

        var changes = result.Monitors[0].Changes;
        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual("brightness",        changes[0].Setting);
        Assert.AreEqual(CliProfileChange.StatusApplied,     changes[0].Status);
        Assert.AreEqual("color-temperature", changes[1].Setting);
        Assert.AreEqual(CliProfileChange.StatusUnsupported, changes[1].Status);
    }
}
