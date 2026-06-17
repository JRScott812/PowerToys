// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Output;

namespace PowerDisplay.Cli.UnitTests;

[TestClass]
public class JsonOutputTests
{
    [TestMethod]
    public void SetResult_HasVersionAndCamelCaseKeys_OnStdout()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var json = new JsonCliOutput(stdout, stderr);

        json.WriteSetResult(new CliSetResult
        {
            Monitor = new CliMonitorRef { Number = 1, Id = "A", Name = "Dell", Method = "DDC/CI" },
            Setting = "brightness",
            BeforeRaw = 30,
            AfterRaw = 50,
            BeforeDisplay = "30%",
            AfterDisplay = "50%",
        });

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.AreEqual("1.0", doc.RootElement.GetProperty("version").GetString());
        Assert.AreEqual("set", doc.RootElement.GetProperty("command").GetString());
        Assert.AreEqual(50, doc.RootElement.GetProperty("afterRaw").GetInt32());
        Assert.AreEqual(string.Empty, stderr.ToString());
    }

    [TestMethod]
    public void ErrorResult_GoesToStderr_NotStdout()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var json = new JsonCliOutput(stdout, stderr);

        json.WriteError(new CliErrorResult
        {
            Command = "set",
            Error = new CliError { Code = CliErrorCodes.OutOfRange, ExitCode = CliExitCodes.OutOfRange, Message = "x" },
        });

        Assert.AreEqual(string.Empty, stdout.ToString());
        var doc = JsonDocument.Parse(stderr.ToString());
        Assert.IsFalse(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.AreEqual("OUT_OF_RANGE", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [TestMethod]
    public void SetResult_NullBefore_IsOmitted()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var json = new JsonCliOutput(stdout, stderr);

        json.WriteSetResult(new CliSetResult
        {
            Monitor = new CliMonitorRef { Number = 1, Id = "A", Name = "Dell", Method = "DDC/CI" },
            Setting = "brightness",
            BeforeRaw = null,
            AfterRaw = 50,
            BeforeDisplay = null,
            AfterDisplay = "50%",
        });

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.IsFalse(doc.RootElement.TryGetProperty("beforeRaw", out _));
        Assert.IsFalse(doc.RootElement.TryGetProperty("beforeDisplay", out _));
    }

    [TestMethod]
    public void ListResult_HasCamelCaseSupportsFlags_OnStdout()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var json = new JsonCliOutput(stdout, stderr);

        json.WriteListResult(new CliListResult
        {
            Monitors =
            [
                new CliListMonitor
                {
                    Number = 1,
                    Id = "A",
                    Name = "Dell",
                    Method = "DDC/CI",
                    SupportsBrightness = true,
                    SupportsVolume = false,
                    SupportsOrientation = true,
                },
            ],
        });

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.AreEqual("list", doc.RootElement.GetProperty("command").GetString());
        var m0 = doc.RootElement.GetProperty("monitors")[0];
        Assert.IsTrue(m0.GetProperty("supportsBrightness").GetBoolean());
        Assert.IsFalse(m0.GetProperty("supportsVolume").GetBoolean());
        Assert.IsTrue(m0.GetProperty("supportsOrientation").GetBoolean());
        Assert.AreEqual(string.Empty, stderr.ToString());
    }

    [TestMethod]
    public void GetResult_NestedSettingsShape_OnStdout()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var json = new JsonCliOutput(stdout, stderr);

        json.WriteGetResult(new CliGetResult
        {
            Monitors =
            [
                new CliGetMonitorEntry
                {
                    Monitor = new CliMonitorRef { Number = 1, Id = "A", Name = "Dell", Method = "DDC/CI" },
                    Settings =
                    [
                        new CliSettingValue { Setting = "orientation", Raw = 90, Display = "90°", Supported = true },
                    ],
                },
            ],
        });

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.AreEqual("get", doc.RootElement.GetProperty("command").GetString());
        var s0 = doc.RootElement.GetProperty("monitors")[0].GetProperty("settings")[0];
        Assert.AreEqual("orientation", s0.GetProperty("setting").GetString());
        Assert.AreEqual(90, s0.GetProperty("raw").GetInt32());
        Assert.IsTrue(s0.GetProperty("supported").GetBoolean());
    }

    [TestMethod]
    public void CapabilitiesResult_VcpCodesShape_OnStdout()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var json = new JsonCliOutput(stdout, stderr);

        json.WriteCapabilitiesResult(new CliCapabilitiesResult
        {
            // capabilities carries transport in communicationMethod, not monitor.method.
            Monitor = new CliMonitorRef { Number = 1, Id = "A", Name = "Dell" },
            CommunicationMethod = "DDC/CI",
            VcpCodes =
            [
                new CliVcpCodeInfo { Code = "0x10", Name = "Brightness", Continuous = true },
                new CliVcpCodeInfo { Code = "0x60", Name = "Input Source", Continuous = false, DiscreteValues = ["HDMI-1 (0x11)"] },
            ],
        });

        var doc = JsonDocument.Parse(stdout.ToString());
        Assert.AreEqual("capabilities", doc.RootElement.GetProperty("command").GetString());
        Assert.AreEqual("DDC/CI", doc.RootElement.GetProperty("communicationMethod").GetString());

        // Transport is not duplicated inside the monitor ref (null Method is omitted).
        Assert.IsFalse(doc.RootElement.GetProperty("monitor").TryGetProperty("method", out _));

        var codes = doc.RootElement.GetProperty("vcpCodes");
        Assert.IsTrue(codes[0].GetProperty("continuous").GetBoolean());
        Assert.IsFalse(codes[0].TryGetProperty("discreteValues", out _)); // null omitted
        Assert.AreEqual("HDMI-1 (0x11)", codes[1].GetProperty("discreteValues")[0].GetString());
    }
}
