// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedCommon;
using PowerDisplay.Cli.Commands;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Options;
using PowerDisplay.Cli.Output;
using PowerDisplay.Cli.Properties;
using PowerDisplay.Cli.Settings;
using PowerDisplay.Common.Services;

namespace PowerDisplay.Cli;

public static class Program
{
    private const int DefaultTimeoutSeconds = 30;

    public static async Task<int> Main(string[] args)
    {
        // Emit UTF-8 so non-ASCII glyphs in human-readable output (the → arrow, ° degree sign,
        // … ellipsis) and any UTF-8 JSON render correctly instead of as '?' on legacy code pages.
        TrySetUtf8Output();

        var root = BuildRootCommand();
        var parser = new Parser(root);
        var parseResult = parser.Parse(args);

        // Help / version short-circuit through the default invocation pipeline (which owns
        // the version + help renderers). Done BEFORE the logger is created so a pure
        // --help/--version invocation has no file-system side effects.
        if (parseResult.Tokens.Count == 0 || HasHelpToken(parseResult) || IsVersionRequest(parseResult))
        {
            return await root.InvokeAsync(args);
        }

        var useJson = parseResult.GetValueForOption(CliOptions.Json);
        var quiet = parseResult.GetValueForOption(CliOptions.Quiet);
        ICliOutput output = useJson ? new JsonCliOutput(quiet) : new TextCliOutput(quiet);

        if (parseResult.Errors.Count > 0)
        {
            // System.CommandLine can report several parse errors for one bad invocation; collapse
            // them into a single envelope so --json consumers always receive exactly one parseable
            // object (and text consumers a single Error line) instead of N concatenated ones.
            output.WriteError(BuildParseErrorResult(
                parseResult.CommandResult.Command.Name,
                parseResult.Errors.Select(e => e.Message)));

            return CliExitCodes.ArgumentError;
        }

        // Logs go to %LOCALAPPDATA%\Microsoft\PowerToys\PowerDisplay\Logs\<version>.
        // Guard initialization: an unwritable log path (locked profile, full disk, policy
        // redirection) creates the directory / trace listener eagerly and would otherwise throw
        // here — OUTSIDE the try below — crashing with a raw stack trace and bypassing the
        // single-envelope error contract. The requested operation does not need the log file,
        // so degrade to no file listener and continue.
        try
        {
            Logger.InitializeLogger("\\PowerDisplay\\Logs");
        }
        catch (Exception)
        {
        }

        var timeoutSeconds = parseResult.GetValueForOption(CliOptions.TimeoutSeconds) ?? DefaultTimeoutSeconds;
        var timedOut = false;
        Timer? timeoutTimer = null;

        try
        {
            using var monitorManager = new MonitorManager();
            using var cts = new CancellationTokenSource();

            var runtime = CliSettingsReader.Read();
            var maxCompatOverride = parseResult.GetValueForOption(CliOptions.MaxCompatibility);
            monitorManager.SetMaxCompatibilityMode(maxCompatOverride ?? runtime.MaxCompatibilityMode);

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            };

            if (timeoutSeconds > 0)
            {
                // `timedOut` is set on the timer thread before cts.Cancel(); the cancel→token
                // propagation establishes happens-before, so the catch below reads it reliably.
                timeoutTimer = new Timer(
                    _ =>
                    {
                        timedOut = true;
                        try
                        {
                            cts.Cancel();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    },
                    null,
                    TimeSpan.FromSeconds(timeoutSeconds),
                    Timeout.InfiniteTimeSpan);
            }

            var command = parseResult.CommandResult.Command;

            Task<int> DispatchAsync()
            {
                if (command == root.ListCommand)
                {
                    return ListCommand.RunAsync(monitorManager, runtime.HiddenMonitorIds, output, cts.Token);
                }

                if (command == root.CapabilitiesCommand)
                {
                    return CapabilitiesCommand.RunAsync(
                        monitorManager,
                        runtime.HiddenMonitorIds,
                        parseResult.GetValueForOption(CliOptions.MonitorNumber),
                        parseResult.GetValueForOption(CliOptions.MonitorId),
                        output,
                        cts.Token);
                }

                if (command == root.GetCommand)
                {
                    return GetCommand.RunAsync(
                        monitorManager,
                        runtime.HiddenMonitorIds,
                        parseResult.GetValueForOption(CliOptions.MonitorNumber),
                        parseResult.GetValueForOption(CliOptions.MonitorId),
                        parseResult.GetValueForOption(CliOptions.SettingFilter),
                        output,
                        cts.Token);
                }

                if (command == root.SetCommand)
                {
                    var inputs = new SetCommandInputs
                    {
                        MonitorNumber = parseResult.GetValueForOption(CliOptions.MonitorNumber),
                        MonitorId = parseResult.GetValueForOption(CliOptions.MonitorId),
                        Brightness = parseResult.GetValueForOption(CliOptions.Brightness),
                        Contrast = parseResult.GetValueForOption(CliOptions.Contrast),
                        Volume = parseResult.GetValueForOption(CliOptions.Volume),
                        ColorTemperature = parseResult.GetValueForOption(CliOptions.ColorTemperature),
                        InputSource = parseResult.GetValueForOption(CliOptions.InputSource),
                        PowerState = parseResult.GetValueForOption(CliOptions.PowerState),
                        Orientation = parseResult.GetValueForOption(CliOptions.Orientation),
                        ConfirmPowerOff = parseResult.GetValueForOption(CliOptions.ConfirmPowerOff),
                    };

                    return SetCommand.RunAsync(monitorManager, runtime.HiddenMonitorIds, inputs, output, cts.Token);
                }

                if (command == root.ProfilesCommand)
                {
                    return Task.FromResult(ProfilesCommand.Run(ProfileService.LoadProfiles(), output));
                }

                if (command == root.ApplyProfileCommand)
                {
                    return ApplyProfileCommand.RunAsync(
                        monitorManager,
                        runtime.HiddenMonitorIds,
                        ProfileService.LoadProfiles(),
                        parseResult.GetValueForArgument(CliOptions.ProfileName),
                        output,
                        cts.Token);
                }

                return root.InvokeAsync(args);
            }

            var commandTask = DispatchAsync();

            // Race the command against cancellation (the timeout timer OR Ctrl+C, both of which cancel
            // `cts`). A wedged synchronous DDC/CI call cannot observe the token, so without this race a
            // single unresponsive monitor would block the process past the deadline. If cancellation
            // wins, report TIMEOUT and exit hard: we deliberately skip the `using` disposal below
            // because a background thread may still be inside a blocking native call against the
            // handles MonitorManager would otherwise free (a use-after-free on teardown). The orphaned
            // call is abandoned with the exiting process.
            var cancellationWaiter = Task.Delay(Timeout.Infinite, cts.Token);
            var finished = await Task.WhenAny(commandTask, cancellationWaiter);
            if (finished != commandTask && !commandTask.IsCompleted)
            {
                output.WriteError(BuildTimeoutErrorResult(parseResult.CommandResult.Command.Name, timedOut, timeoutSeconds));
                Console.Out.Flush();
                Console.Error.Flush();
                Environment.Exit(CliExitCodes.Timeout);
            }

            // Awaiting the completed task surfaces cooperative cancellation (discovery checkpoints or
            // the post-write token check) as OperationCanceledException → handled below as TIMEOUT.
            return await commandTask;
        }
        catch (OperationCanceledException)
        {
            output.WriteError(BuildTimeoutErrorResult(parseResult.CommandResult.Command.Name, timedOut, timeoutSeconds));
            return CliExitCodes.Timeout;
        }
        catch (Exception ex)
        {
            Logger.LogError($"PowerDisplay CLI failed: {ex}");
            output.WriteError(new CliErrorResult
            {
                Command = parseResult.CommandResult.Command.Name,
                Error = new CliError
                {
                    Code = CliErrorCodes.InternalError,
                    ExitCode = CliExitCodes.InternalError,
                    Message = Resources.Error_UnexpectedError(ex.Message),
                },
            });
            return CliExitCodes.InternalError;
        }
        finally
        {
            timeoutTimer?.Dispose();
        }
    }

    public static bool HasHelpToken(ParseResult parseResult)
        => parseResult.UnmatchedTokens.Any(IsHelpToken)
            || HelpBoundToProfileNameArgument(parseResult);

    private static bool IsHelpToken(string token)
        => token is "--help" or "-h" or "-?" or "/?";

    // The `apply-profile <name>` positional argument greedily captures a "--help" token (it binds to
    // the argument, so it never reaches UnmatchedTokens). Without this, `apply-profile --help` would
    // be dispatched as "apply a profile literally named --help" instead of printing help like every
    // other command. Option *values* that look like help (e.g. `set -i -h`) are unaffected: they are
    // matched to an option, not to this argument.
    private static bool HelpBoundToProfileNameArgument(ParseResult parseResult)
        => parseResult.CommandResult.Command.Name == "apply-profile"
            && IsHelpToken(parseResult.GetValueForArgument(CliOptions.ProfileName) ?? string.Empty);

    public static bool HasVersionToken(ParseResult parseResult)
        => parseResult.UnmatchedTokens.Any(t => t == "--version");

    public static bool IsVersionRequest(ParseResult parseResult)
        => HasVersionToken(parseResult) && parseResult.CommandResult.Command is RootCommand;

    /// <summary>
    /// Collapses one or more System.CommandLine parse-error messages into a single
    /// <see cref="CliErrorResult"/> so the error stream stays a single parseable envelope.
    /// </summary>
    public static CliErrorResult BuildParseErrorResult(string command, IEnumerable<string> messages)
    {
        var combined = string.Join("; ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
        return new CliErrorResult
        {
            Command = command,
            Error = new CliError
            {
                Code = CliErrorCodes.ArgumentError,
                ExitCode = CliExitCodes.ArgumentError,
                Message = combined.Length == 0 ? Resources.Error_InvalidArguments : combined,
            },
        };
    }

    // Shared TIMEOUT envelope for both the cancellation-race path and the OperationCanceledException catch.
    private static CliErrorResult BuildTimeoutErrorResult(string command, bool timedOut, int timeoutSeconds)
        => new()
        {
            Command = command,
            Error = new CliError
            {
                Code = CliErrorCodes.Timeout,
                ExitCode = CliExitCodes.Timeout,
                Message = timedOut
                    ? Resources.Error_TimedOut(timeoutSeconds)
                    : Resources.Error_Cancelled,
            },
        };

    private static void TrySetUtf8Output()
    {
        try
        {
            // UTF-8 without a BOM: a leading BOM in redirected/piped output would corrupt --json
            // for consumers that don't strip it (e.g. some JSON parsers and shells).
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // No real console attached (handles redirected/closed); leave the default encoding.
        }
        catch (System.Security.SecurityException)
        {
            // Host policy forbids changing console encoding; not fatal for the operation.
        }
    }

    private static PowerDisplayRootCommand BuildRootCommand() => new();
}
