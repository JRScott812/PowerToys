// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PowerDisplay.Contracts;

namespace PowerDisplay.Cli.Ipc;

/// <summary>
/// CLI-side named-pipe client that connects to the running PowerDisplay app, sends one request
/// line, reads one response line, and returns <see langword="null"/> on connect failure or timeout.
/// <para>
/// <b>Protocol:</b> BOM-less UTF-16 LE encoding, <c>'\n'</c>-delimited lines, one request → one response.
/// Mirrors the app-side <c>CliPipeServer</c> in <c>PowerDisplay/Ipc/CliPipeServer.cs</c>.
/// </para>
/// </summary>
/// <remarks>
/// [UNVERIFIED] Not compiled (no VS C++ toolchain via CLI->Lib->interop chain); build+verify on dev box.
/// </remarks>
public sealed class CliPipeClient
{
    /// <summary>
    /// Connects to the PowerDisplay named-pipe server, sends <paramref name="requestJson"/>,
    /// and returns the response JSON line.
    /// </summary>
    /// <param name="requestJson">The JSON-encoded request to send.</param>
    /// <param name="connectTimeout">How long to wait for the pipe server to accept the connection.</param>
    /// <param name="ct">Cancellation token; <see cref="OperationCanceledException"/> propagates to the caller.</param>
    /// <returns>
    /// The response JSON line on success; <see langword="null"/> when the app is not running,
    /// the pipe is unavailable, or the connection timed out.
    /// </returns>
    // BOM-less UTF-16 LE — must match CliPipeServer.  Encoding.Unicode emits a BOM on the first
    // write which corrupts line-framing on named pipes; this encoding is identical in every other
    // respect (UTF-16 LE, 2 bytes per ASCII char).
    private static readonly Encoding _pipeEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

    public async Task<string?> SendAsync(string requestJson, TimeSpan connectTimeout, CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeNames.CliServer(), PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync((int)connectTimeout.TotalMilliseconds, ct);

            using var writer = new StreamWriter(client, _pipeEncoding, 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, _pipeEncoding, false, 1024, leaveOpen: true);

            await writer.WriteLineAsync(requestJson.AsMemory(), ct);
            return await reader.ReadLineAsync(ct);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        // OperationCanceledException is intentionally NOT caught here — it propagates to the
        // caller, which treats Ctrl+C / timeout-token cancellation as user cancellation.
    }
}
