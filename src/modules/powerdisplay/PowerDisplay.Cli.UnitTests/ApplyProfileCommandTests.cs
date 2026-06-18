// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Cli.Commands;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;
using PowerDisplay.Cli.UnitTests.Fakes;
using PowerDisplay.Common.Models;
using PowerDisplay.Models;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.UnitTests;

[TestClass]
public class ApplyProfileCommandTests
{
    private static readonly IReadOnlySet<string> NoHidden = new HashSet<string>();

    private sealed class CapturingOutput : ICliOutput
    {
        public CliApplyProfileResult? Apply { get; private set; }

        public CliErrorResult? Error { get; private set; }

        public void WriteListResult(CliListResult result)
        {
        }

        public void WriteSetResult(CliSetResult result)
        {
        }

        public void WriteGetResult(CliGetResult result)
        {
        }

        public void WriteCapabilitiesResult(CliCapabilitiesResult result)
        {
        }

        public void WriteProfileListResult(CliProfileListResult result)
        {
        }

        public void WriteApplyProfileResult(CliApplyProfileResult result) => Apply = result;

        public void WriteError(CliErrorResult result) => Error = result;

        public void WriteWarning(string message)
        {
        }
    }

    private static Monitor FullMonitor(string id = "MON-1") => new()
    {
        MonitorNumber = 1,
        Id = id,
        Name = "Dell",
        CommunicationMethod = "DDC/CI",
        Capabilities = MonitorCapabilities.Brightness | MonitorCapabilities.Contrast | MonitorCapabilities.Volume,
        SupportsColorTemperature = true,
    };

    private static PowerDisplayProfiles WithProfile(string name, params ProfileMonitorSetting[] settings)
    {
        var profiles = new PowerDisplayProfiles();
        profiles.Profiles.Add(new PowerDisplayProfile(name, settings.ToList()));
        return profiles;
    }

    [TestMethod]
    public async Task Apply_AllSettings_WritesAndReportsApplied()
    {
        var mm = new FakeMonitorManager(FullMonitor());
        var profiles = WithProfile("Night", new ProfileMonitorSetting("MON-1", brightness: 40, colorTemperatureVcp: 0x05, contrast: 60, volume: 30));
        var output = new CapturingOutput();

        var exit = await ApplyProfileCommand.RunAsync(mm, NoHidden, profiles, "Night", output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.IsNotNull(output.Apply);
        Assert.AreEqual("Night", output.Apply!.Profile);
        Assert.IsTrue(output.Apply.Ok);

        var outcome = output.Apply.Monitors[0];
        Assert.IsTrue(outcome.Connected);
        Assert.AreEqual(4, outcome.Changes.Count);
        Assert.IsTrue(outcome.Changes.All(c => c.Status == CliProfileChange.StatusApplied));

        CollectionAssert.Contains(mm.Writes, ("brightness", "MON-1", 40));
        CollectionAssert.Contains(mm.Writes, ("contrast", "MON-1", 60));
        CollectionAssert.Contains(mm.Writes, ("volume", "MON-1", 30));
        CollectionAssert.Contains(mm.Writes, ("color-temperature", "MON-1", 0x05));
    }

    [TestMethod]
    public async Task Apply_ProfileNotFound_ReturnsArgumentError_NoWrite()
    {
        var mm = new FakeMonitorManager(FullMonitor());
        var output = new CapturingOutput();

        var exit = await ApplyProfileCommand.RunAsync(mm, NoHidden, new PowerDisplayProfiles(), "Ghost", output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.ArgumentError, exit);
        Assert.IsNotNull(output.Error);
        Assert.AreEqual(CliErrorCodes.ArgumentError, output.Error!.Error.Code);
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Apply_MonitorNotConnected_SkipsWithoutWrite()
    {
        var mm = new FakeMonitorManager(FullMonitor("MON-1"));
        var profiles = WithProfile("Night", new ProfileMonitorSetting("MON-MISSING", brightness: 40));
        var output = new CapturingOutput();

        var exit = await ApplyProfileCommand.RunAsync(mm, NoHidden, profiles, "Night", output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        var outcome = output.Apply!.Monitors[0];
        Assert.IsFalse(outcome.Connected);
        Assert.AreEqual(0, outcome.Changes.Count);
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Apply_UnsupportedSetting_SkippedButOthersApply()
    {
        // Monitor supports only brightness; the profile also asks for contrast.
        var monitor = new Monitor
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
            Capabilities = MonitorCapabilities.Brightness,
        };
        var mm = new FakeMonitorManager(monitor);
        var profiles = WithProfile("Night", new ProfileMonitorSetting("MON-1", brightness: 40, contrast: 60));
        var output = new CapturingOutput();

        var exit = await ApplyProfileCommand.RunAsync(mm, NoHidden, profiles, "Night", output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        var changes = output.Apply!.Monitors[0].Changes;
        Assert.AreEqual(CliProfileChange.StatusApplied, changes.Single(c => c.Setting == "brightness").Status);
        Assert.AreEqual(CliProfileChange.StatusUnsupported, changes.Single(c => c.Setting == "contrast").Status);
        CollectionAssert.Contains(mm.Writes, ("brightness", "MON-1", 40));
        Assert.IsFalse(mm.Writes.Any(w => w.Op == "contrast"));
    }

    [TestMethod]
    public async Task Apply_HiddenMonitor_NotTargeted_ReportedNotConnected()
    {
        // A profile that targets a hidden monitor is treated like a disconnected one: ExcludeHidden
        // drops it from discovery, so it is reported not-connected and nothing is written.
        var mm = new FakeMonitorManager(FullMonitor("MON-1"));
        var profiles = WithProfile("Night", new ProfileMonitorSetting("MON-1", brightness: 40));
        var hidden = new HashSet<string> { "MON-1" };
        var output = new CapturingOutput();

        var exit = await ApplyProfileCommand.RunAsync(mm, hidden, profiles, "Night", output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.IsFalse(output.Apply!.Monitors[0].Connected);
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Apply_HardwareFailure_ReturnsHardwareFailure()
    {
        var mm = new FakeMonitorManager(FullMonitor()) { FailWrites = true };
        var profiles = WithProfile("Night", new ProfileMonitorSetting("MON-1", brightness: 40));
        var output = new CapturingOutput();

        var exit = await ApplyProfileCommand.RunAsync(mm, NoHidden, profiles, "Night", output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.HardwareFailure, exit);
        Assert.IsFalse(output.Apply!.Ok);
        Assert.AreEqual(CliProfileChange.StatusHardwareFailure, output.Apply.Monitors[0].Changes[0].Status);
    }

    [TestMethod]
    public async Task Apply_OutOfRangeValue_SkippedAndReturnsOutOfRange()
    {
        var mm = new FakeMonitorManager(FullMonitor());
        var profiles = WithProfile("Night", new ProfileMonitorSetting("MON-1", brightness: 150));
        var output = new CapturingOutput();

        var exit = await ApplyProfileCommand.RunAsync(mm, NoHidden, profiles, "Night", output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.OutOfRange, exit);
        Assert.AreEqual(CliProfileChange.StatusOutOfRange, output.Apply!.Monitors[0].Changes[0].Status);
        Assert.AreEqual(0, mm.Writes.Count);
    }
}
