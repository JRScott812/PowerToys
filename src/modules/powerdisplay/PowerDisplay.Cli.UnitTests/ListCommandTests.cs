// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Cli.Commands;
using PowerDisplay.Cli.Output;
using PowerDisplay.Cli.UnitTests.Fakes;
using PowerDisplay.Common.Models;
using Monitor = PowerDisplay.Common.Models.Monitor;

namespace PowerDisplay.Cli.UnitTests;

[TestClass]
public class ListCommandTests
{
    private sealed class Capturing : ICliOutput
    {
        public CliListResult? List { get; private set; }

        public void WriteListResult(CliListResult result) => List = result;

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

        public void WriteApplyProfileResult(CliApplyProfileResult result)
        {
        }

        public void WriteError(CliErrorResult result)
        {
        }

        public void WriteWarning(string message)
        {
        }
    }

    private static Monitor Mon(int n, string id, string gdi)
        => new()
        {
            MonitorNumber = n,
            Id = id,
            Name = "M" + n,
            CommunicationMethod = "DDC/CI",
            GdiDeviceName = gdi,
            Capabilities = MonitorCapabilities.Brightness,
        };

    [TestMethod]
    public async Task List_IncludesOrientationSupportFromGdiName()
    {
        var mm = new FakeMonitorManager(Mon(1, "A", @"\\.\DISPLAY1"), Mon(2, "B", string.Empty));
        var output = new Capturing();

        var exit = await ListCommand.RunAsync(mm, new HashSet<string>(), output, CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.IsTrue(output.List!.Monitors[0].SupportsOrientation);
        Assert.IsFalse(output.List.Monitors[1].SupportsOrientation);
    }

    [TestMethod]
    public async Task List_ExcludesHiddenMonitors()
    {
        var mm = new FakeMonitorManager(Mon(1, "A", @"\\.\DISPLAY1"), Mon(2, "B", @"\\.\DISPLAY2"));
        var output = new Capturing();

        var exit = await ListCommand.RunAsync(mm, new HashSet<string> { "A" }, output, CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.AreEqual(1, output.List!.Monitors.Count);
        Assert.AreEqual("B", output.List.Monitors[0].Id);
    }

    [TestMethod]
    public async Task List_EmptyDiscovery_ReturnsEmpty()
    {
        var mm = new FakeMonitorManager();
        var output = new Capturing();

        var exit = await ListCommand.RunAsync(mm, new HashSet<string>(), output, CancellationToken.None);

        Assert.AreEqual(0, exit);
        Assert.AreEqual(0, output.List!.Monitors.Count);
    }
}
