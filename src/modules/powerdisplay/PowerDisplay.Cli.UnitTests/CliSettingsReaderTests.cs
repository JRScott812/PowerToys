// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Cli.Settings;

namespace PowerDisplay.Cli.UnitTests;

[TestClass]
public class CliSettingsReaderTests
{
    [TestMethod]
    public void Read_MissingFile_ReturnsDefault()
    {
        var result = CliSettingsReader.Read(Path.Combine(Path.GetTempPath(), "pd-cli-does-not-exist.json"));
        Assert.IsFalse(result.MaxCompatibilityMode);
        Assert.AreEqual(0, result.HiddenMonitorIds.Count);
    }

    [TestMethod]
    public void Read_RealSettingsShape_ParsesMaxCompatAndHidden()
    {
        var settings = new PowerDisplaySettings();
        settings.Properties.MaxCompatibilityMode = true;
        settings.Properties.Monitors.Add(new MonitorInfo { Id = "MON-A", IsHidden = true });
        settings.Properties.Monitors.Add(new MonitorInfo { Id = "MON-B", IsHidden = false });

        var tmp = Path.Combine(Path.GetTempPath(), "pd-cli-settings-test.json");
        File.WriteAllText(tmp, settings.ToJsonString());
        try
        {
            var result = CliSettingsReader.Read(tmp);

            Assert.IsTrue(result.MaxCompatibilityMode);
            Assert.IsTrue(result.HiddenMonitorIds.Contains("MON-A"));
            Assert.IsFalse(result.HiddenMonitorIds.Contains("MON-B"));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [TestMethod]
    public void Read_HiddenMonitorWithNullId_IsExcluded()
    {
        var settings = new PowerDisplaySettings();
        settings.Properties.Monitors.Add(new MonitorInfo { Id = "MON-A", IsHidden = true });
        settings.Properties.Monitors.Add(new MonitorInfo { Id = string.Empty, IsHidden = true });

        var tmp = Path.Combine(Path.GetTempPath(), "pd-cli-settings-nullid.json");
        File.WriteAllText(tmp, settings.ToJsonString());
        try
        {
            var result = CliSettingsReader.Read(tmp);
            Assert.AreEqual(1, result.HiddenMonitorIds.Count);
            Assert.IsTrue(result.HiddenMonitorIds.Contains("MON-A"));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [TestMethod]
    public void Read_HiddenIdMatch_IsCaseInsensitive()
    {
        // The reader builds the hidden-id set with StringComparer.OrdinalIgnoreCase; a regression
        // dropping that would silently stop hiding monitors whose discovered id casing differs.
        var settings = new PowerDisplaySettings();
        settings.Properties.Monitors.Add(new MonitorInfo { Id = "MON-A", IsHidden = true });

        var tmp = Path.Combine(Path.GetTempPath(), "pd-cli-settings-caseinsensitive.json");
        File.WriteAllText(tmp, settings.ToJsonString());
        try
        {
            var result = CliSettingsReader.Read(tmp);
            Assert.IsTrue(result.HiddenMonitorIds.Contains("mon-a"));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [TestMethod]
    public void Read_MalformedJson_ReturnsDefault()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "pd-cli-settings-malformed.json");
        File.WriteAllText(tmp, "{ this is not valid json ");
        try
        {
            var result = CliSettingsReader.Read(tmp);
            Assert.IsFalse(result.MaxCompatibilityMode);
            Assert.AreEqual(0, result.HiddenMonitorIds.Count);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
