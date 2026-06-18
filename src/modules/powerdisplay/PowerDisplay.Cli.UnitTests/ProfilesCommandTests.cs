// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Cli.Commands;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;
using PowerDisplay.Models;

namespace PowerDisplay.Cli.UnitTests;

[TestClass]
public class ProfilesCommandTests
{
    private sealed class CapturingOutput : ICliOutput
    {
        public CliProfileListResult? List { get; private set; }

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

        public void WriteProfileListResult(CliProfileListResult result) => List = result;

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

    [TestMethod]
    public void Run_NoProfiles_EmitsEmptyList()
    {
        var output = new CapturingOutput();

        var exit = ProfilesCommand.Run(new PowerDisplayProfiles(), output);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.IsNotNull(output.List);
        Assert.AreEqual(0, output.List!.Profiles.Count);
    }

    [TestMethod]
    public void Run_ListsProfilesWithMonitorCount()
    {
        var profiles = new PowerDisplayProfiles();
        profiles.Profiles.Add(new PowerDisplayProfile("Night", new List<ProfileMonitorSetting>
        {
            new ProfileMonitorSetting("MON-1", brightness: 20),
            new ProfileMonitorSetting("MON-2", brightness: 30),
        }));
        profiles.Profiles.Add(new PowerDisplayProfile("Day", new List<ProfileMonitorSetting>
        {
            new ProfileMonitorSetting("MON-1", brightness: 80),
        }));
        var output = new CapturingOutput();

        var exit = ProfilesCommand.Run(profiles, output);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(2, output.List!.Profiles.Count);
        Assert.AreEqual("Night", output.List.Profiles[0].Name);
        Assert.AreEqual(2, output.List.Profiles[0].MonitorCount);
        Assert.AreEqual("Day", output.List.Profiles[1].Name);
        Assert.AreEqual(1, output.List.Profiles[1].MonitorCount);
    }
}
