// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedCommon;
using PowerDisplay.Cli.Commands;
using PowerDisplay.Cli.Errors;
using PowerDisplay.Cli.Options;
using PowerDisplay.Cli.Output;
using PowerDisplay.Cli.Settings;
using PowerDisplay.Common.Services;

namespace PowerDisplay.Cli;

public static class Program
{
    private const int DefaultTimeoutSeconds = 30;

    public static async Task<int> Main(string[] args)
    {
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
            foreach (var err in parseResult.Errors)
            {
                output.WriteError(new CliErrorResult
                {
                    Command = parseResult.CommandResult.Command.Name,
                    Error = new CliError
                    {
                        Code = CliErrorCodes.ArgumentError,
                        ExitCode = CliExitCodes.ArgumentError,
                        Message = err.Message,
                    },
                });
            }

            return CliExitCodes.ArgumentError;
        }

        // Logs go to %LOCALAPPDATA%\Microsoft\PowerToys\PowerDisplay\Logs\<version>.
        Logger.InitializeLogger("\\PowerDisplay\\Logs");

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

            if (command == root.ListCommand)
            {
                return await ListCommand.RunAsync(monitorManager, runtime.HiddenMonitorIds, output, cts.Token);
            }

            if (command == root.CapabilitiesCommand)
            {
                return await CapabilitiesCommand.RunAsync(
                    monitorManager,
                    runtime.HiddenMonitorIds,
                    parseResult.GetValueForOption(CliOptions.MonitorNumber),
                    parseResult.GetValueForOption(CliOptions.MonitorId),
                    output,
                    cts.Token);
            }

            if (command == root.GetCommand)
            {
                return await GetCommand.RunAsync(
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

                return await SetCommand.RunAsync(monitorManager, runtime.HiddenMonitorIds, inputs, output, cts.Token);
            }

            return await root.InvokeAsync(args);
        }
        catch (OperationCanceledException)
        {
            output.WriteError(new CliErrorResult
            {
                Command = parseResult.CommandResult.Command.Name,
                Error = new CliError
                {
                    Code = CliErrorCodes.Timeout,
                    ExitCode = CliExitCodes.Timeout,
                    Message = timedOut
                        ? $"operation timed out after {timeoutSeconds}s"
                        : "operation was cancelled",
                },
            });
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
                    Message = $"unexpected error: {ex.Message}",
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
        => parseResult.UnmatchedTokens.Any(t => t is "--help" or "-h" or "-?" or "/?");

    public static bool HasVersionToken(ParseResult parseResult)
        => parseResult.UnmatchedTokens.Any(t => t == "--version");

    public static bool IsVersionRequest(ParseResult parseResult)
        => HasVersionToken(parseResult) && parseResult.CommandResult.Command is RootCommand;

    private static PowerDisplayRootCommand BuildRootCommand() => new();
}
