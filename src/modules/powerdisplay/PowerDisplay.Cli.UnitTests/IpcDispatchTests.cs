// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// [UNVERIFIED] Not compiled (no VS C++ toolchain via CLI->Lib->interop chain); build+verify on dev box.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PowerDisplay.Cli.Commands;
using PowerDisplay.Cli.Ipc;
using PowerDisplay.Cli.Output;
using PowerDisplay.Contracts;

namespace PowerDisplay.Cli.UnitTests;

/// <summary>
/// Tests the IPC dispatch path: provider-unavailable (null response) → exit 10,
/// success response → rendered and exit 0, and error response → rendered and
/// correct exit code.
/// </summary>
/// <remarks>
/// [UNVERIFIED] Not compiled (no VS C++ toolchain via CLI->Lib->interop chain); build+verify on dev box.
/// </remarks>
[TestClass]
public class IpcDispatchTests
{
    private static readonly TimeSpan AnyTimeout = TimeSpan.FromSeconds(30);

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class CaptureOutput : ICliOutput
    {
        public readonly List<string> StdoutLines = new();
        public readonly List<string> StderrLines = new();

        private readonly StringWriter _stdout = new();
        private readonly StringWriter _stderr = new();

        public void WriteListResult(CliListResult r) => StdoutLines.Add("list:" + r.Command);
        public void WriteSetResult(CliSetResult r) => StdoutLines.Add("set:" + r.Setting);
        public void WriteGetResult(CliGetResult r) => StdoutLines.Add("get");
        public void WriteCapabilitiesResult(CliCapabilitiesResult r) => StdoutLines.Add("capabilities");
        public void WriteProfileListResult(CliProfileListResult r) => StdoutLines.Add("profiles");
        public void WriteApplyProfileResult(CliApplyProfileResult r) => StdoutLines.Add("apply-profile:" + r.Ok);
        public void WriteError(CliErrorResult r) => StderrLines.Add("error:" + r.Error.Code + ":" + r.Error.ExitCode);
        public void WriteWarning(string message) => StderrLines.Add("warn:" + message);
    }

    private static IpcDispatcher MakeDispatcher(string? stubResponse, CaptureOutput output)
    {
        Task<string?> StubSend(string _, TimeSpan __, CancellationToken ___) =>
            Task.FromResult(stubResponse);
        return new IpcDispatcher(StubSend, output, AnyTimeout);
    }

    private static string SerializeSuccess<T>(T obj, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(obj, typeInfo);

    private static string SerializeError(CliErrorResult err)
        => JsonSerializer.Serialize(err, ContractsJsonContext.Default.CliErrorResult);

    // ── ProviderUnavailable (null) ────────────────────────────────────────────

    [TestMethod]
    public async Task When_provider_unavailable_list_exits_10()
    {
        var output = new CaptureOutput();
        var dispatcher = MakeDispatcher(null, output);
        var exit = await dispatcher.SendListAsync(CliRequestBuilder.BuildList(), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.ProviderUnavailable, exit);
        Assert.AreEqual(1, output.StderrLines.Count);
        StringAssert.Contains(output.StderrLines[0], CliErrorCodes.ProviderUnavailable);
        StringAssert.Contains(output.StderrLines[0], "10");
    }

    [TestMethod]
    public async Task When_provider_unavailable_get_exits_10()
    {
        var output = new CaptureOutput();
        var dispatcher = MakeDispatcher(null, output);
        var exit = await dispatcher.SendGetAsync(CliRequestBuilder.BuildGet(null, null, null), CancellationToken.None);
        Assert.AreEqual(CliExitCodes.ProviderUnavailable, exit);
    }

    [TestMethod]
    public async Task When_provider_unavailable_set_exits_10()
    {
        var output = new CaptureOutput();
        var dispatcher = MakeDispatcher(null, output);
        var inputs = new SetCommandInputs { Brightness = 50 };
        var exit = await dispatcher.SendSetAsync(CliRequestBuilder.BuildSet(inputs), CancellationToken.None);
        Assert.AreEqual(CliExitCodes.ProviderUnavailable, exit);
    }

    [TestMethod]
    public async Task When_provider_unavailable_capabilities_exits_10()
    {
        var output = new CaptureOutput();
        var dispatcher = MakeDispatcher(null, output);
        var exit = await dispatcher.SendCapabilitiesAsync(CliRequestBuilder.BuildCapabilities(1, null), CancellationToken.None);
        Assert.AreEqual(CliExitCodes.ProviderUnavailable, exit);
    }

    [TestMethod]
    public async Task When_provider_unavailable_profiles_exits_10()
    {
        var output = new CaptureOutput();
        var dispatcher = MakeDispatcher(null, output);
        var exit = await dispatcher.SendProfilesAsync(CliRequestBuilder.BuildProfiles(), CancellationToken.None);
        Assert.AreEqual(CliExitCodes.ProviderUnavailable, exit);
    }

    [TestMethod]
    public async Task When_provider_unavailable_apply_profile_exits_10()
    {
        var output = new CaptureOutput();
        var dispatcher = MakeDispatcher(null, output);
        var exit = await dispatcher.SendApplyProfileAsync(CliRequestBuilder.BuildApplyProfile("Night"), CancellationToken.None);
        Assert.AreEqual(CliExitCodes.ProviderUnavailable, exit);
    }

    // ── Success responses rendered, exit 0 ───────────────────────────────────

    [TestMethod]
    public async Task Success_list_renders_result_exits_0()
    {
        var output = new CaptureOutput();
        var responseJson = SerializeSuccess(new CliListResult { Ok = true, Monitors = [] }, ContractsJsonContext.Default.CliListResult);
        var dispatcher = MakeDispatcher(responseJson, output);
        var exit = await dispatcher.SendListAsync(CliRequestBuilder.BuildList(), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(1, output.StdoutLines.Count);
        StringAssert.Contains(output.StdoutLines[0], "list");
    }

    [TestMethod]
    public async Task Success_get_renders_result_exits_0()
    {
        var output = new CaptureOutput();
        var responseJson = SerializeSuccess(new CliGetResult { Ok = true, Monitors = [] }, ContractsJsonContext.Default.CliGetResult);
        var dispatcher = MakeDispatcher(responseJson, output);
        var exit = await dispatcher.SendGetAsync(CliRequestBuilder.BuildGet(null, null, null), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(1, output.StdoutLines.Count);
    }

    [TestMethod]
    public async Task Success_set_renders_result_exits_0()
    {
        var output = new CaptureOutput();
        var responseJson = SerializeSuccess(
            new CliSetResult { Ok = true, Setting = "brightness", Monitor = new CliMonitorRef { Number = 1, Id = "x", Name = "N" }, AfterRaw = 80, AfterDisplay = "80%" },
            ContractsJsonContext.Default.CliSetResult);
        var dispatcher = MakeDispatcher(responseJson, output);
        var inputs = new SetCommandInputs { Brightness = 80 };
        var exit = await dispatcher.SendSetAsync(CliRequestBuilder.BuildSet(inputs), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
        Assert.AreEqual(1, output.StdoutLines.Count);
        StringAssert.Contains(output.StdoutLines[0], "brightness");
    }

    // ── Error responses rendered, correct exit code ───────────────────────────

    [TestMethod]
    public async Task Error_response_renders_error_and_returns_its_exit_code()
    {
        var output = new CaptureOutput();
        var errorResponse = new CliErrorResult
        {
            Ok = false,
            Command = "list",
            Error = new CliError
            {
                Code = CliErrorCodes.MonitorNotFound,
                ExitCode = CliExitCodes.MonitorNotFound,
                Message = "Monitor not found.",
            },
        };
        var responseJson = SerializeError(errorResponse);
        var dispatcher = MakeDispatcher(responseJson, output);
        var exit = await dispatcher.SendListAsync(CliRequestBuilder.BuildList(), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.MonitorNotFound, exit);
        Assert.AreEqual(1, output.StderrLines.Count);
        StringAssert.Contains(output.StderrLines[0], CliErrorCodes.MonitorNotFound);
    }

    [TestMethod]
    public async Task Error_response_hardware_failure_returns_exit_5()
    {
        var output = new CaptureOutput();
        var errorResponse = new CliErrorResult
        {
            Ok = false,
            Command = "set",
            Error = new CliError
            {
                Code = CliErrorCodes.HardwareFailure,
                ExitCode = CliExitCodes.HardwareFailure,
                Message = "DDC/CI write failed.",
            },
        };
        var responseJson = SerializeError(errorResponse);
        var dispatcher = MakeDispatcher(responseJson, output);
        var inputs = new SetCommandInputs { Brightness = 80 };
        var exit = await dispatcher.SendSetAsync(CliRequestBuilder.BuildSet(inputs), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.HardwareFailure, exit);
    }

    // ── apply-profile exit-code carried through IPC ───────────────────────────

    /// <summary>
    /// Verifies that when the app returns a canned CliApplyProfileResult with Ok=false and
    /// ExitCode=2 (OutOfRange), the CLI dispatcher returns exit 2, NOT the old hardcoded 5
    /// (HardwareFailure). This is the regression test for the apply-profile exit-code bug.
    /// [UNVERIFIED] Not compiled (no VS C++ toolchain via CLI->Lib->interop chain); build+verify on dev box.
    /// </summary>
    [TestMethod]
    public async Task ApplyProfile_OutOfRange_partial_failure_exits_2()
    {
        var output = new CaptureOutput();
        var responseJson = SerializeSuccess(
            new CliApplyProfileResult
            {
                Ok = false,
                ExitCode = CliExitCodes.OutOfRange,
                Profile = "Night",
                Monitors = new List<CliProfileMonitorOutcome>
                {
                    new CliProfileMonitorOutcome
                    {
                        Monitor = new CliMonitorRef { Number = 1, Id = "MON1", Name = "Monitor A" },
                        Connected = true,
                        Changes = new List<CliProfileChange>
                        {
                            new CliProfileChange { Setting = "brightness", Value = 110, Status = CliProfileChange.StatusOutOfRange },
                        },
                    },
                },
            },
            ContractsJsonContext.Default.CliApplyProfileResult);
        var dispatcher = MakeDispatcher(responseJson, output);
        var exit = await dispatcher.SendApplyProfileAsync(CliRequestBuilder.BuildApplyProfile("Night"), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.OutOfRange, exit, "OutOfRange partial failure must return exit 2, not hardcoded HardwareFailure(5)");
        Assert.AreEqual(1, output.StdoutLines.Count);
    }

    [TestMethod]
    public async Task ApplyProfile_HardwareFailure_exits_5()
    {
        var output = new CaptureOutput();
        var responseJson = SerializeSuccess(
            new CliApplyProfileResult
            {
                Ok = false,
                ExitCode = CliExitCodes.HardwareFailure,
                Profile = "Gaming",
                Monitors = new List<CliProfileMonitorOutcome>(),
            },
            ContractsJsonContext.Default.CliApplyProfileResult);
        var dispatcher = MakeDispatcher(responseJson, output);
        var exit = await dispatcher.SendApplyProfileAsync(CliRequestBuilder.BuildApplyProfile("Gaming"), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.HardwareFailure, exit);
    }

    [TestMethod]
    public async Task ApplyProfile_full_success_exits_0()
    {
        var output = new CaptureOutput();
        var responseJson = SerializeSuccess(
            new CliApplyProfileResult
            {
                Ok = true,
                ExitCode = CliExitCodes.Ok,
                Profile = "Work",
                Monitors = new List<CliProfileMonitorOutcome>(),
            },
            ContractsJsonContext.Default.CliApplyProfileResult);
        var dispatcher = MakeDispatcher(responseJson, output);
        var exit = await dispatcher.SendApplyProfileAsync(CliRequestBuilder.BuildApplyProfile("Work"), CancellationToken.None);

        Assert.AreEqual(CliExitCodes.Ok, exit);
    }

    // ── CliRequestBuilder round-trips ────────────────────────────────────────

    [TestMethod]
    public void BuildSet_Brightness_MapsCorrectly()
    {
        var inputs = new SetCommandInputs { Brightness = 75, MonitorNumber = 2 };
        var envelope = CliRequestBuilder.BuildSet(inputs);

        Assert.AreEqual(CliCommandNames.Set, envelope.Command);
        Assert.IsNotNull(envelope.Set);
        Assert.AreEqual("brightness", envelope.Set!.Setting);
        Assert.AreEqual("75", envelope.Set.RawValue);
        Assert.AreEqual(2, envelope.Set.MonitorNumber);
    }

    [TestMethod]
    public void BuildSet_PowerState_MapsCorrectly()
    {
        var inputs = new SetCommandInputs { PowerState = "Standby", ConfirmPowerOff = true };
        var envelope = CliRequestBuilder.BuildSet(inputs);

        Assert.AreEqual("power-state", envelope.Set!.Setting);
        Assert.AreEqual("Standby", envelope.Set.RawValue);
        Assert.IsTrue(envelope.Set.ConfirmPowerOff);
    }

    [TestMethod]
    public void BuildSet_NoSetting_Throws()
    {
        var inputs = new SetCommandInputs();
        Assert.ThrowsException<InvalidOperationException>(() => CliRequestBuilder.BuildSet(inputs));
    }

    [TestMethod]
    public void BuildGet_Maps_MonitorSelectors_And_Filter()
    {
        var envelope = CliRequestBuilder.BuildGet(3, "myId", "brightness");
        Assert.AreEqual(CliCommandNames.Get, envelope.Command);
        Assert.AreEqual(3, envelope.Get!.MonitorNumber);
        Assert.AreEqual("myId", envelope.Get.MonitorId);
        Assert.AreEqual("brightness", envelope.Get.SettingFilter);
    }

    [TestMethod]
    public void BuildApplyProfile_Maps_ProfileName()
    {
        var envelope = CliRequestBuilder.BuildApplyProfile("Night");
        Assert.AreEqual(CliCommandNames.ApplyProfile, envelope.Command);
        Assert.AreEqual("Night", envelope.ApplyProfile!.ProfileName);
    }
}
