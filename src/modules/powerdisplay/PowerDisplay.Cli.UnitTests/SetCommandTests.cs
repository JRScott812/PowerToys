// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Cli.Commands;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;
using PowerDisplay.Cli.UnitTests.Fakes;
using PowerDisplay.Common.Models;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.UnitTests;

[TestClass]
public class SetCommandTests
{
    private static readonly IReadOnlySet<string> NoHidden = new HashSet<string>();

    private static readonly int[] PowerStateSupportedValues = { 0x01, 0x04 };

    private sealed class CapturingOutput : ICliOutput
    {
        public CliSetResult? Set { get; private set; }

        public CliErrorResult? Error { get; private set; }

        public void WriteListResult(CliListResult result)
        {
        }

        public void WriteSetResult(CliSetResult result) => Set = result;

        public void WriteGetResult(CliGetResult result)
        {
        }

        public void WriteCapabilitiesResult(CliCapabilitiesResult result)
        {
        }

        public void WriteError(CliErrorResult result) => Error = result;

        public void WriteWarning(string message)
        {
        }
    }

    private static Monitor BrightnessMonitor(MonitorReadFlags read = MonitorReadFlags.Brightness)
        => new()
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
            CurrentBrightness = 30,
            Capabilities = MonitorCapabilities.Brightness,
            ReadValues = read,
        };

    private static Monitor PowerStateMonitor()
    {
        var caps = new VcpCapabilities();
        caps.SupportedVcpCodes[0xD6] = new VcpCodeInfo(0xD6, "Power Mode", PowerStateSupportedValues);
        return new Monitor
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
            CurrentPowerState = 0x01,
            VcpCapabilitiesInfo = caps,
            ReadValues = MonitorReadFlags.PowerState,
        };
    }

    [TestMethod]
    public async Task Set_Brightness_Success_ReportsBeforeAfter()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Brightness = 50 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(30, output.Set!.BeforeRaw);
        Assert.AreEqual(50, output.Set.AfterRaw);
        Assert.AreEqual("50%", output.Set.AfterDisplay);
        Assert.AreEqual(("brightness", "MON-1", 50), mm.Writes[0]);
    }

    [TestMethod]
    public async Task Set_Brightness_HardwareFailure_ReturnsHardwareFailure()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor()) { FailWrites = true };
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Brightness = 50 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.HardwareFailure, exit);
        Assert.AreEqual(CliErrorCodes.HardwareFailure, output.Error!.Error.Code);
    }

    [TestMethod]
    public async Task Set_Brightness_ReadUnknown_ReportsNullBefore()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor(MonitorReadFlags.None));
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Brightness = 50 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.IsNull(output.Set!.BeforeRaw);
        Assert.IsNull(output.Set.BeforeDisplay);
    }

    [TestMethod]
    public async Task Set_OutOfRange_ReturnsOutOfRange_NoWrite()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Brightness = 150 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.OutOfRange, exit);
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Set_NoSetting_ReturnsArgumentError()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.ArgumentError, exit);
    }

    [TestMethod]
    public async Task Set_MonitorNotFound_ReturnsMonitorNotFound()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 99, Brightness = 50 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.MonitorNotFound, exit);
    }

    [TestMethod]
    public async Task Set_PowerOff_WithoutConfirm_IsRejected()
    {
        var mm = new FakeMonitorManager(PowerStateMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, PowerState = "0x04" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.ArgumentError, exit);
        Assert.AreEqual(0, mm.Writes.Count);
        StringAssert.Contains(output.Error!.Error.Hint, "--confirm-power-off");
    }

    [TestMethod]
    public async Task Set_PowerOff_WithConfirm_IsApplied()
    {
        var mm = new FakeMonitorManager(PowerStateMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, PowerState = "0x04", ConfirmPowerOff = true }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(("power-state", "MON-1", 0x04), mm.Writes[0]);
    }

    [TestMethod]
    public async Task Set_PowerOn_DoesNotRequireConfirm()
    {
        var mm = new FakeMonitorManager(PowerStateMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, PowerState = "0x01" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(("power-state", "MON-1", 0x01), mm.Writes[0]);
    }

    [TestMethod]
    public async Task Set_HiddenMonitor_CannotBeTargeted()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, new HashSet<string> { "MON-1" }, new SetCommandInputs { MonitorNumber = 1, Brightness = 50 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.MonitorNotFound, exit);
        Assert.AreEqual(0, mm.Writes.Count);
    }
}
