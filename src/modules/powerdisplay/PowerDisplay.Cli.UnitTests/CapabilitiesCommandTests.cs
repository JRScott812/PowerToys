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
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.UnitTests;

[TestClass]
public class CapabilitiesCommandTests
{
    private static readonly int[] PowerStates = { 0x01, 0x04 };

    private sealed class Capturing : ICliOutput
    {
        public CliCapabilitiesResult? Caps { get; private set; }

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

        public void WriteCapabilitiesResult(CliCapabilitiesResult result) => Caps = result;

        public void WriteError(CliErrorResult result) => Error = result;

        public void WriteWarning(string message)
        {
        }
    }

    private static Monitor MonitorWithCaps()
    {
        var caps = new VcpCapabilities();
        caps.SupportedVcpCodes[0x10] = new VcpCodeInfo(0x10, "Brightness", null);
        caps.SupportedVcpCodes[0xD6] = new VcpCodeInfo(0xD6, "Power Mode", PowerStates);
        return new Monitor
        {
            MonitorNumber = 1,
            Id = "MON-1",
            Name = "Dell",
            CommunicationMethod = "DDC/CI",
            VcpCapabilitiesInfo = caps,
        };
    }

    [TestMethod]
    public async Task Capabilities_NoSelector_ReturnsSelectorMissing()
    {
        var mm = new FakeMonitorManager(MonitorWithCaps());
        var output = new Capturing();

        var exit = await CapabilitiesCommand.RunAsync(mm, new HashSet<string>(), null, null, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.SelectorMissing, exit);
        Assert.AreEqual(CliErrorCodes.SelectorMissing, output.Error!.Error.Code);
    }

    [TestMethod]
    public async Task Capabilities_ResolvedMonitor_ReturnsVcpCodes()
    {
        var mm = new FakeMonitorManager(MonitorWithCaps());
        var output = new Capturing();

        var exit = await CapabilitiesCommand.RunAsync(mm, new HashSet<string>(), 1, null, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.IsNotNull(output.Caps);
        Assert.AreEqual(1, output.Caps!.Monitor.Number);
        Assert.AreEqual("DDC/CI", output.Caps.CommunicationMethod);
        Assert.IsTrue(output.Caps.VcpCodes.Count >= 2);
    }

    [TestMethod]
    public async Task Capabilities_DistinguishesContinuousAndDiscreteCodes()
    {
        var mm = new FakeMonitorManager(MonitorWithCaps());
        var output = new Capturing();

        var exit = await CapabilitiesCommand.RunAsync(mm, new HashSet<string>(), 1, null, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);

        var brightness = output.Caps!.VcpCodes.Single(c => c.Code == "0x10");
        Assert.IsTrue(brightness.Continuous);
        Assert.IsNull(brightness.DiscreteValues);

        var power = output.Caps.VcpCodes.Single(c => c.Code == "0xD6");
        Assert.IsFalse(power.Continuous);
        Assert.IsNotNull(power.DiscreteValues);
        CollectionAssert.Contains(power.DiscreteValues!.ToList(), "On (0x01)");
        CollectionAssert.Contains(power.DiscreteValues!.ToList(), "Off (DPM) (0x04)");
    }

    [TestMethod]
    public async Task Capabilities_HiddenMonitor_NotFound()
    {
        var mm = new FakeMonitorManager(MonitorWithCaps());
        var output = new Capturing();

        var exit = await CapabilitiesCommand.RunAsync(mm, new HashSet<string> { "MON-1" }, 1, null, output, CancellationToken.None);

        Assert.AreEqual(CliExitCodes.MonitorNotFound, exit);
    }
}
