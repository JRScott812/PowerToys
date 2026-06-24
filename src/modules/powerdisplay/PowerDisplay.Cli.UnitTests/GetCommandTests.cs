// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
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
public class GetCommandTests
{
    private static readonly IReadOnlySet<string> NoHidden = new HashSet<string>();

    private sealed class CapturingOutput : ICliOutput
    {
        public CliGetResult? LastGetResult { get; private set; }

        public CliErrorResult? LastErrorResult { get; private set; }

        public string? LastWarning { get; private set; }

        public void WriteListResult(CliListResult result)
        {
        }

        public void WriteSetResult(CliSetResult result)
        {
        }

        public void WriteGetResult(CliGetResult result) => LastGetResult = result;

        public void WriteCapabilitiesResult(CliCapabilitiesResult result)
        {
        }

        public void WriteProfileListResult(CliProfileListResult result)
        {
        }

        public void WriteApplyProfileResult(CliApplyProfileResult result)
        {
        }

        public void WriteError(CliErrorResult result) => LastErrorResult = result;

        public void WriteWarning(string message) => LastWarning = message;
    }

    private static Monitor Sample(
        int number,
        string id,
        string name,
        string method,
        MonitorReadFlags read = MonitorReadFlags.Brightness | MonitorReadFlags.Orientation)
    {
        var m = new Monitor
        {
            MonitorNumber = number,
            Id = id,
            Name = name,
            CommunicationMethod = method,
            CurrentBrightness = 30,
            CurrentContrast = 50,
            CurrentVolume = 70,
            CurrentColorTemperature = 0x05,
            CurrentInputSource = 0x11,
            CurrentPowerState = 0x01,
            GdiDeviceName = @"\\.\DISPLAY1",
            ReadValues = read,
        };

        // Only brightness is in the capability set, so brightness + orientation (GdiDeviceName)
        // are the supported settings; the rest report supported:false.
        m.Capabilities = MonitorCapabilities.Brightness;
        return m;
    }

    [TestMethod]
    public void EmitAll_EveryMonitorAppearsAsEntry_WithMethod()
    {
        var monitors = new List<Monitor>
        {
            Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI"),
            Sample(2, "\\\\?\\DISPLAY#B", "Internal", "WMI"),
        };
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll(monitors, settingFilter: null, output);

        Assert.AreEqual(0, exit);
        Assert.IsNotNull(output.LastGetResult);
        Assert.AreEqual(2, output.LastGetResult!.Monitors.Count);

        Assert.AreEqual(1, output.LastGetResult.Monitors[0].Monitor.Number);
        Assert.AreEqual("Dell", output.LastGetResult.Monitors[0].Monitor.Name);
        Assert.AreEqual("DDC/CI", output.LastGetResult.Monitors[0].Monitor.Method);

        Assert.AreEqual(2, output.LastGetResult.Monitors[1].Monitor.Number);
        Assert.AreEqual("WMI", output.LastGetResult.Monitors[1].Monitor.Method);
    }

    [TestMethod]
    public void EmitAll_PerEntryHasAllSettings()
    {
        var monitors = new List<Monitor> { Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI") };
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll(monitors, settingFilter: null, output);

        Assert.AreEqual(0, exit);
        Assert.AreEqual(GetCommand.AllSettingNames.Length, output.LastGetResult!.Monitors[0].Settings.Count);
    }

    [TestMethod]
    public void EmitAll_WithSettingFilter_OnlyEmitsThatSetting()
    {
        var monitors = new List<Monitor> { Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI") };
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll(monitors, settingFilter: "brightness", output);

        Assert.AreEqual(0, exit);
        Assert.AreEqual(1, output.LastGetResult!.Monitors[0].Settings.Count);
        Assert.AreEqual("brightness", output.LastGetResult.Monitors[0].Settings[0].Setting);
        Assert.AreEqual("30%", output.LastGetResult.Monitors[0].Settings[0].Display);
    }

    [TestMethod]
    public void EmitAll_UnknownSetting_ReturnsArgumentError_WithHint()
    {
        var monitors = new List<Monitor> { Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI") };
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll(monitors, settingFilter: "bogus", output);

        Assert.AreEqual(CliExitCodes.ArgumentError, exit);
        Assert.IsNotNull(output.LastErrorResult);
        Assert.AreEqual(CliErrorCodes.ArgumentError, output.LastErrorResult!.Error.Code);
        StringAssert.Contains(output.LastErrorResult.Error.Hint, "brightness");
    }

    [TestMethod]
    public void EmitAll_EmptyMonitorList_ReturnsEmptyResult()
    {
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll([], settingFilter: null, output);

        Assert.AreEqual(0, exit);
        Assert.IsNotNull(output.LastGetResult);
        Assert.AreEqual(0, output.LastGetResult!.Monitors.Count);
    }

    [TestMethod]
    public void EmitAll_OrientationRaw_IsDegreesNotIndex()
    {
        var m = Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI");
        m.Orientation = 1; // index 1 == 90 degrees
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll(new List<Monitor> { m }, settingFilter: "orientation", output);

        Assert.AreEqual(0, exit);
        var setting = output.LastGetResult!.Monitors[0].Settings[0];
        Assert.AreEqual("orientation", setting.Setting);
        Assert.AreEqual(90, setting.Raw);   // degrees, not the index 1
        Assert.AreEqual("90°", setting.Display);
    }

    [TestMethod]
    public void EmitAll_SupportedButUnread_OmitsValue()
    {
        // Brightness is advertised (supported) but discovery never read it: report supported and
        // omit the value rather than passing off the seeded default as a live reading.
        var monitors = new List<Monitor> { Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI", read: MonitorReadFlags.None) };
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll(monitors, settingFilter: "brightness", output);

        Assert.AreEqual(0, exit);
        var setting = output.LastGetResult!.Monitors[0].Settings[0];
        Assert.IsTrue(setting.Supported);
        Assert.IsNull(setting.Raw);
        Assert.IsNull(setting.Display);
    }

    [TestMethod]
    public void EmitAll_UnsupportedSetting_OmitsValue()
    {
        // Contrast is not in the capability set: supported:false and no value, matching the text
        // renderer's "(not supported)" rather than emitting a misleading raw:50/"50%".
        var monitors = new List<Monitor> { Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI") };
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll(monitors, settingFilter: "contrast", output);

        Assert.AreEqual(0, exit);
        var setting = output.LastGetResult!.Monitors[0].Settings[0];
        Assert.IsFalse(setting.Supported);
        Assert.IsNull(setting.Raw);
        Assert.IsNull(setting.Display);
    }

    [TestMethod]
    public void TextOutput_EmptyResult_ResolvesLocalizedNoMonitorsString()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var writer = new TextCliOutput(stdout, stderr);

        writer.WriteGetResult(new CliGetResult { Monitors = [] });

        // Exercises ResourceManager end-to-end: the string comes from Resources.resx, so a wrong
        // base name would return the resource key instead and fail this assertion.
        StringAssert.Contains(stdout.ToString(), "No monitors discovered.");
    }

    [TestMethod]
    public void TextOutput_RendersProtocolAndIdHeader()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var writer = new TextCliOutput(stdout, stderr);

        var result = new CliGetResult
        {
            Monitors =
            [
                new CliGetMonitorEntry
                {
                    Monitor = new CliMonitorRef
                    {
                        Number = 1,
                        Id = "\\\\?\\DISPLAY#A",
                        Name = "Dell",
                        Method = "DDC/CI",
                    },
                    Settings =
                    [
                        new CliSettingValue { Setting = "brightness", Raw = 30, Display = "30%", Supported = true },
                    ],
                },
            ],
        };

        writer.WriteGetResult(result);

        var text = stdout.ToString();
        StringAssert.Contains(text, "Monitor 1 (Dell)");
        StringAssert.Contains(text, "protocol");
        StringAssert.Contains(text, "DDC/CI");
        StringAssert.Contains(text, "id");
        StringAssert.Contains(text, "\\\\?\\DISPLAY#A");
        StringAssert.Contains(text, "brightness");
        StringAssert.Contains(text, "30%");
    }

    [TestMethod]
    public void EmitAll_SettingFilterIsCaseInsensitive()
    {
        var monitors = new List<Monitor> { Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI") };
        var output = new CapturingOutput();

        // Mixed-case input must resolve to the canonical lower-case setting, not error out.
        var exit = GetCommand.EmitAll(monitors, settingFilter: "Brightness", output);

        Assert.AreEqual(0, exit);
        Assert.IsNotNull(output.LastGetResult);
        Assert.AreEqual(1, output.LastGetResult!.Monitors[0].Settings.Count);
        Assert.AreEqual("brightness", output.LastGetResult.Monitors[0].Settings[0].Setting);
    }

    [TestMethod]
    public void EmitAll_UnknownSetting_ErrorEchoesOriginalInput_WithoutMonitorRef()
    {
        var monitors = new List<Monitor> { Sample(1, "\\\\?\\DISPLAY#A", "Dell", "DDC/CI") };
        var output = new CapturingOutput();

        var exit = GetCommand.EmitAll(monitors, settingFilter: "Brightnesss", output);

        Assert.AreEqual(CliExitCodes.ArgumentError, exit);

        // The error echoes the user's original casing, and a monitor-independent argument error
        // is not pinned to whichever monitor was enumerated first.
        StringAssert.Contains(output.LastErrorResult!.Error.Message, "Brightnesss");
        Assert.IsNull(output.LastErrorResult.Monitor);
    }

    [TestMethod]
    public async Task RunAsync_ByNumber_EmitsOnlySelectedMonitor()
    {
        var mm = new FakeMonitorManager(
            Sample(1, "MON-1", "Dell", "DDC/CI"),
            Sample(2, "MON-2", "Internal", "WMI"));
        var output = new CapturingOutput();

        var exit = await GetCommand.RunAsync(mm, NoHidden, monitorNumber: 2, monitorId: null, settingFilter: null, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(1, output.LastGetResult!.Monitors.Count);
        Assert.AreEqual(2, output.LastGetResult.Monitors[0].Monitor.Number);
    }

    [TestMethod]
    public async Task RunAsync_NoSelector_EmitsAllMonitors()
    {
        var mm = new FakeMonitorManager(
            Sample(1, "MON-1", "Dell", "DDC/CI"),
            Sample(2, "MON-2", "Internal", "WMI"));
        var output = new CapturingOutput();

        var exit = await GetCommand.RunAsync(mm, NoHidden, monitorNumber: null, monitorId: null, settingFilter: null, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(2, output.LastGetResult!.Monitors.Count);
    }

    [TestMethod]
    public async Task RunAsync_HiddenMonitor_NotTargetable_ReturnsMonitorNotFound()
    {
        // Guards that ExcludeHidden runs before ResolveSelected on the selected path.
        var mm = new FakeMonitorManager(Sample(1, "MON-1", "Dell", "DDC/CI"));
        var output = new CapturingOutput();
        var hidden = new HashSet<string> { "MON-1" };

        var exit = await GetCommand.RunAsync(mm, hidden, monitorNumber: 1, monitorId: null, settingFilter: null, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.MonitorNotFound, exit);
    }

    [TestMethod]
    public async Task RunAsync_BothSelectors_IdWins_SurfacesWarning()
    {
        var mm = new FakeMonitorManager(
            Sample(1, "MON-1", "Dell", "DDC/CI"),
            Sample(2, "MON-2", "Internal", "WMI"));
        var output = new CapturingOutput();

        var exit = await GetCommand.RunAsync(mm, NoHidden, monitorNumber: 1, monitorId: "MON-2", settingFilter: null, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(2, output.LastGetResult!.Monitors[0].Monitor.Number);
        Assert.IsNotNull(output.LastWarning);
    }

    [TestMethod]
    public async Task RunAsync_SelectedMonitor_UnknownSetting_ReturnsArgumentError()
    {
        var mm = new FakeMonitorManager(Sample(1, "MON-1", "Dell", "DDC/CI"));
        var output = new CapturingOutput();

        var exit = await GetCommand.RunAsync(mm, NoHidden, monitorNumber: 1, monitorId: null, settingFilter: "bogus", output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.ArgumentError, exit);
        Assert.IsNotNull(output.LastErrorResult);
        Assert.AreEqual(CliErrorCodes.ArgumentError, output.LastErrorResult!.Error.Code);
    }
}
