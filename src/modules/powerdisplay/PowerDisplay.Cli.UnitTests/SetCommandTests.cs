// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Cli.Commands;
using PowerDisplay.Contracts;
using PowerDisplay.Cli.UnitTests.Fakes;
using PowerDisplay.Common.Models;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.UnitTests;

[TestClass]
public class SetCommandTests
{
    private static readonly IReadOnlySet<string> NoHidden = new HashSet<string>();

    private static readonly int[] PowerStateSupportedValues = { 0x01, 0x04 };

    private static readonly int[] PowerStateWithStandbyValues = { 0x01, 0x02, 0x04 };

    private static readonly int[] ColorTemperatureSupportedValues = { 0x01, 0x05, 0x08 };

    private static readonly int[] InputSourceSupportedValues = { 0x0F, 0x11, 0x12 };

    private sealed class CapturingOutput : ICliOutput
    {
        public CliSetResult? Set { get; private set; }

        public CliErrorResult? Error { get; private set; }

        public string? Warning { get; private set; }

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

        public void WriteProfileListResult(CliProfileListResult result)
        {
        }

        public void WriteApplyProfileResult(CliApplyProfileResult result)
        {
        }

        public void WriteError(CliErrorResult result) => Error = result;

        public void WriteWarning(string message) => Warning = message;
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

    private static Monitor PowerStateMonitor(int[]? supportedValues = null)
    {
        var caps = new VcpCapabilities();
        caps.SupportedVcpCodes[0xD6] = new VcpCodeInfo(0xD6, "Power Mode", supportedValues ?? PowerStateSupportedValues);
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

    private static Monitor OrientationMonitor(MonitorReadFlags read = MonitorReadFlags.Orientation, int orientation = 1)
        => new()
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
            GdiDeviceName = @"\\.\DISPLAY1",
            Orientation = orientation,
            ReadValues = read,
        };

    private static Monitor VolumeMonitor()
        => new()
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
            CurrentVolume = 20,
            Capabilities = MonitorCapabilities.Volume,
            ReadValues = MonitorReadFlags.Volume,
        };

    private static Monitor ColorTemperatureMonitor()
    {
        var caps = new VcpCapabilities();
        caps.SupportedVcpCodes[0x14] = new VcpCodeInfo(0x14, "Select Color Preset", ColorTemperatureSupportedValues);
        return new Monitor
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
            SupportsColorTemperature = true,
            CurrentColorTemperature = 0x05,
            VcpCapabilitiesInfo = caps,
            ReadValues = MonitorReadFlags.ColorTemperature,
        };
    }

    private static Monitor InputSourceMonitor(int[]? supportedValues = null)
    {
        var caps = new VcpCapabilities();
        caps.SupportedVcpCodes[0x60] = new VcpCodeInfo(0x60, "Input Source", supportedValues ?? InputSourceSupportedValues);
        return new Monitor
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
            CurrentInputSource = 0x0F,
            VcpCapabilitiesInfo = caps,
            ReadValues = MonitorReadFlags.InputSource,
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

    [TestMethod]
    public async Task Set_PowerOff_UnsupportedMonitor_ReportsUnsupportedNotConfirm()
    {
        // Monitor with no power-state capability (no VcpCapabilitiesInfo for 0xD6).
        var m = new Monitor
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
        };
        var mm = new FakeMonitorManager(m);
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, PowerState = "0x04" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.UnsupportedFeature, exit);
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Set_DiscreteReadUnknown_ReportsNullBefore()
    {
        // Power-state supported, but the value was never successfully read (ReadValues=None).
        var m = PowerStateMonitor();
        m.ReadValues = MonitorReadFlags.None;
        var mm = new FakeMonitorManager(m);
        var output = new CapturingOutput();

        // 0x01 (On) needs no confirmation and is in the supported set.
        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, PowerState = "0x01" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.IsNull(output.Set!.BeforeRaw);
        Assert.IsNull(output.Set.BeforeDisplay);
        Assert.AreEqual(0x01, output.Set.AfterRaw);
    }

    [TestMethod]
    public async Task Set_Orientation_Success_ReportsBeforeAfterDegrees()
    {
        var mm = new FakeMonitorManager(OrientationMonitor(orientation: 1)); // currently 90°
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Orientation = "180" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(90, output.Set!.BeforeRaw);
        Assert.AreEqual("90°", output.Set.BeforeDisplay);
        Assert.AreEqual(180, output.Set.AfterRaw);
        Assert.AreEqual("180°", output.Set.AfterDisplay);
        Assert.AreEqual(("orientation", "MON-1", 2), mm.Writes[0]); // 180° == rotation index 2
    }

    [TestMethod]
    public async Task Set_Orientation_ReadUnknown_OmitsBefore()
    {
        // GdiDeviceName present (rotation possible) but the live orientation was never read.
        var mm = new FakeMonitorManager(OrientationMonitor(MonitorReadFlags.None, orientation: 0));
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Orientation = "90" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.IsNull(output.Set!.BeforeRaw);
        Assert.IsNull(output.Set.BeforeDisplay);
        Assert.AreEqual(90, output.Set.AfterRaw);
    }

    [TestMethod]
    public async Task Set_TimedOutWrite_SurfacesCancellation_NotFalseSuccess()
    {
        // Models a write that overran --timeout: the token is cancelled but the (fake) hardware
        // write still returns success. The command must surface cancellation, not a false success.
        var mm = new FakeMonitorManager(BrightnessMonitor());
        var output = new CapturingOutput();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelled = false;
        try
        {
            await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Brightness = 50 }, output, cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert.IsTrue(cancelled, "a write that overran the deadline must surface cancellation");
        Assert.IsNull(output.Set); // no success envelope emitted
    }

    [TestMethod]
    public async Task Set_PowerOff_NotInSupportedSet_ReturnsInvalidDiscrete_NotConfirmation()
    {
        // Monitor advertises VCP 0xD6 but only the On (0x01) value — Off (DPM)/0x04 is unsupported.
        // The value check must run BEFORE the confirmation gate, so the user gets the real exit 3
        // instead of being asked to --confirm-power-off for a value that the monitor can never accept.
        var onlyOn = new[] { 0x01 };
        var mm = new FakeMonitorManager(PowerStateMonitor(onlyOn));
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, PowerState = "0x04" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.InvalidDiscreteValue, exit);
        Assert.AreEqual(CliErrorCodes.InvalidDiscreteValue, output.Error!.Error.Code);
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Set_MultipleSettings_ReturnsArgumentError_NoWrite()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Brightness = 50, Contrast = 70 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.ArgumentError, exit);
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Set_Contrast_OnBrightnessOnlyMonitor_ReturnsUnsupportedFeature()
    {
        var mm = new FakeMonitorManager(BrightnessMonitor()); // advertises only Brightness
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Contrast = 50 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.UnsupportedFeature, exit);
        StringAssert.Contains(output.Error!.Error.Message, "contrast");
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Set_BothSelectors_IdWins_EmitsWarning()
    {
        var nMon = BrightnessMonitor(); // number 1, id MON-1
        var idMon = new Monitor
        {
            MonitorNumber = 2,
            Id = "MON-2",
            Name = "Internal",
            CommunicationMethod = "DDC/CI",
            CurrentBrightness = 40,
            Capabilities = MonitorCapabilities.Brightness,
            ReadValues = MonitorReadFlags.Brightness,
        };
        var mm = new FakeMonitorManager(nMon, idMon);
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(
            mm,
            NoHidden,
            new SetCommandInputs { MonitorNumber = 1, MonitorId = "MON-2", Brightness = 55 },
            output,
            CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(1, mm.Writes.Count); // the ignored -n target was never written
        Assert.AreEqual(("brightness", "MON-2", 55), mm.Writes[0]); // -i target, not -n
        Assert.IsNotNull(output.Warning);
        StringAssert.Contains(output.Warning!, "ignored");
    }

    [TestMethod]
    public async Task Set_Volume_Success_ReportsBeforeAfter()
    {
        var mm = new FakeMonitorManager(VolumeMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, Volume = 35 }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(20, output.Set!.BeforeRaw);
        Assert.AreEqual(35, output.Set.AfterRaw);
        Assert.AreEqual("35%", output.Set.AfterDisplay);
        Assert.AreEqual(("volume", "MON-1", 35), mm.Writes[0]);
    }

    [TestMethod]
    public async Task Set_ColorTemperature_InSupportedSet_WritesResolvedValue()
    {
        var mm = new FakeMonitorManager(ColorTemperatureMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, ColorTemperature = "0x08" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(0x08, output.Set!.AfterRaw);
        Assert.AreEqual(("color-temperature", "MON-1", 0x08), mm.Writes[0]);
    }

    [TestMethod]
    public async Task Set_InputSource_ByFriendlyName_WritesResolvedVcpValue()
    {
        var mm = new FakeMonitorManager(InputSourceMonitor());
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, InputSource = "HDMI-1" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(0x11, output.Set!.AfterRaw); // HDMI-1 == VCP 0x11
        Assert.AreEqual(("input-source", "MON-1", 0x11), mm.Writes[0]);
    }

    [TestMethod]
    public async Task Set_InputSource_OutOfByteHex_OnUnenumeratedCode_ReturnsInvalidDiscrete_NoWrite()
    {
        // Monitor advertises VCP 0x60 but enumerates no values, so the membership check is
        // skipped; the resolver's byte-range guard must still reject 0x100 (256) before it can
        // be truncated into a wrong byte at the native write.
        var mm = new FakeMonitorManager(InputSourceMonitor(System.Array.Empty<int>()));
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, InputSource = "0x100" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.InvalidDiscreteValue, exit);
        Assert.AreEqual(0, mm.Writes.Count);
    }

    [TestMethod]
    public async Task Set_Standby_WithoutConfirm_IsRejected()
    {
        // Standby (0x02) blanks the panel just like Off, so it must also require --confirm-power-off.
        var mm = new FakeMonitorManager(PowerStateMonitor(PowerStateWithStandbyValues));
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, PowerState = "Standby" }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.ArgumentError, exit);
        Assert.AreEqual(0, mm.Writes.Count);
        StringAssert.Contains(output.Error!.Error.Hint, "--confirm-power-off");
    }

    [TestMethod]
    public async Task Set_Standby_WithConfirm_IsApplied()
    {
        var mm = new FakeMonitorManager(PowerStateMonitor(PowerStateWithStandbyValues));
        var output = new CapturingOutput();

        var exit = await SetCommand.RunAsync(mm, NoHidden, new SetCommandInputs { MonitorNumber = 1, PowerState = "Standby", ConfirmPowerOff = true }, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(("power-state", "MON-1", 0x02), mm.Writes[0]);
    }
}
