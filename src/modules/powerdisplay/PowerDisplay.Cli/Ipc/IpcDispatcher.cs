// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// [UNVERIFIED] Not compiled (no VS C++ toolchain via CLI->Lib->interop chain); build+verify on dev box.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PowerDisplay.Cli.Output;
using PowerDisplay.Cli.Properties;
using PowerDisplay.Contracts;

namespace PowerDisplay.Cli.Ipc;

/// <summary>
/// Encapsulates the common IPC dispatch flow: serialize envelope → send → check
/// provider-unavailable → deserialize response → render → return exit code.
/// <para>
/// The <see cref="SendAsync"/> delegate is injected so the dispatch core can be unit-tested
/// with a stub without standing up a real named-pipe server.
/// </para>
/// </summary>
/// <remarks>
/// [UNVERIFIED] Not compiled (no VS C++ toolchain via CLI->Lib->interop chain); build+verify on dev box.
/// </remarks>
public sealed class IpcDispatcher
{
    /// <summary>
    /// Signature that matches <see cref="CliPipeClient.SendAsync"/>. Inject a stub in tests.
    /// </summary>
    public delegate Task<string?> SendDelegate(string requestJson, TimeSpan connectTimeout, CancellationToken ct);

    private readonly SendDelegate _send;
    private readonly ICliOutput _output;
    private readonly TimeSpan _timeout;

    public IpcDispatcher(SendDelegate send, ICliOutput output, TimeSpan timeout)
    {
        _send = send;
        _output = output;
        _timeout = timeout;
    }

    /// <summary>
    /// Convenience constructor that uses a real <see cref="CliPipeClient"/> instance.
    /// </summary>
    public IpcDispatcher(ICliOutput output, TimeSpan timeout)
        : this(new CliPipeClient().SendAsync, output, timeout)
    {
    }

    // ── per-command dispatch helpers ─────────────────────────────────────────

    public Task<int> SendListAsync(CliRequestEnvelope envelope, CancellationToken ct)
        => SendAndRenderAsync(envelope, ct,
            respJson => Deserialize(respJson, ContractsJsonContext.Default.CliListResult,
                result => { _output.WriteListResult(result); return CliExitCodes.Ok; }));

    public Task<int> SendGetAsync(CliRequestEnvelope envelope, CancellationToken ct)
        => SendAndRenderAsync(envelope, ct,
            respJson => Deserialize(respJson, ContractsJsonContext.Default.CliGetResult,
                result => { _output.WriteGetResult(result); return CliExitCodes.Ok; }));

    public Task<int> SendSetAsync(CliRequestEnvelope envelope, CancellationToken ct)
        => SendAndRenderAsync(envelope, ct,
            respJson => Deserialize(respJson, ContractsJsonContext.Default.CliSetResult,
                result => { _output.WriteSetResult(result); return CliExitCodes.Ok; }));

    public Task<int> SendCapabilitiesAsync(CliRequestEnvelope envelope, CancellationToken ct)
        => SendAndRenderAsync(envelope, ct,
            respJson => Deserialize(respJson, ContractsJsonContext.Default.CliCapabilitiesResult,
                result => { _output.WriteCapabilitiesResult(result); return CliExitCodes.Ok; }));

    public Task<int> SendProfilesAsync(CliRequestEnvelope envelope, CancellationToken ct)
        => SendAndRenderAsync(envelope, ct,
            respJson => Deserialize(respJson, ContractsJsonContext.Default.CliProfileListResult,
                result => { _output.WriteProfileListResult(result); return CliExitCodes.Ok; }));

    public Task<int> SendApplyProfileAsync(CliRequestEnvelope envelope, CancellationToken ct)
        => SendAndRenderAsync(envelope, ct,
            respJson => Deserialize(respJson, ContractsJsonContext.Default.CliApplyProfileResult,
                result =>
                {
                    _output.WriteApplyProfileResult(result);
                    // Return the worst-outcome exit code carried by the DTO (0=Ok, 2=OutOfRange, 5=HardwareFailure).
                    // Previously this was hardcoded to HardwareFailure when Ok=false, losing OutOfRange(2) partials.
                    return result.ExitCode;
                }));

    // ── core flow ────────────────────────────────────────────────────────────

    private async Task<int> SendAndRenderAsync(
        CliRequestEnvelope envelope,
        CancellationToken ct,
        Func<string, int> renderSuccess)
    {
        var requestJson = JsonSerializer.Serialize(envelope, ContractsJsonContext.Default.CliRequestEnvelope);
        var respJson = await _send(requestJson, _timeout, ct);

        if (respJson is null)
        {
            return WriteProviderUnavailable(envelope.Command);
        }

        // Disambiguation strategy:
        //   Genuine error responses: Ok=false AND error.code is a non-empty CliErrorCodes.* value.
        //   apply-profile failure DTOs: Ok=false but NO error object (error.code is empty string);
        //     they carry ExitCode (2 or 5) directly on the CliApplyProfileResult.
        //   All other success DTOs: Ok=true.
        //
        //   We try to deserialize as CliErrorResult first. We only treat it as an error if BOTH
        //   Ok==false AND error.code is non-empty — this prevents misclassifying apply-profile
        //   Ok=false success DTOs (which deserialize with a default-empty error.code) as errors.
        //   Every genuine app-side error path sets error.code to a non-empty CliErrorCodes.* value,
        //   so they still take the error branch. apply-profile failures fall through to the success
        //   path where SendApplyProfileAsync reads result.ExitCode.
        //
        // TODO(localization): map error Code -> localized message CLI-side instead of rendering
        // app-provided Message. For now we render Message/Hint verbatim (byte-identical to English).
        var errorCandidate = TryDeserializeError(respJson);
        if (errorCandidate is not null && !errorCandidate.Ok
            && !string.IsNullOrEmpty(errorCandidate.Error?.Code))
        {
            _output.WriteError(errorCandidate);
            return errorCandidate.Error.ExitCode;
        }

        try
        {
            return renderSuccess(respJson);
        }
        catch (JsonException)
        {
            // The response was not null and not a recognized error, but also failed to deserialize
            // as the expected success type — likely a schema mismatch between CLI and app versions.
            _output.WriteError(BuildInternalError(envelope.Command, "Response could not be deserialized as expected type."));
            return CliExitCodes.InternalError;
        }
    }

    private static CliErrorResult? TryDeserializeError(string respJson)
    {
        try
        {
            return JsonSerializer.Deserialize(respJson, ContractsJsonContext.Default.CliErrorResult);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int Deserialize<T>(
        string respJson,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Func<T, int> render)
        where T : class
    {
        var result = JsonSerializer.Deserialize(respJson, typeInfo)
            ?? throw new JsonException($"Deserialized {typeof(T).Name} was null.");
        return render(result);
    }

    private int WriteProviderUnavailable(string command)
    {
        _output.WriteError(new CliErrorResult
        {
            Ok = false,
            Command = command,
            Error = new CliError
            {
                Code = CliErrorCodes.ProviderUnavailable,
                ExitCode = CliExitCodes.ProviderUnavailable,
                Message = "PowerDisplay is not running. Enable it in PowerToys settings.",
            },
        });
        return CliExitCodes.ProviderUnavailable;
    }

    private static CliErrorResult BuildInternalError(string command, string message) => new()
    {
        Command = command,
        Error = new CliError
        {
            Code = CliErrorCodes.InternalError,
            ExitCode = CliExitCodes.InternalError,
            Message = message,
        },
    };
}
