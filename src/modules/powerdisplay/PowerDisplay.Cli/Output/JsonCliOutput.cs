// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Text.Json;

namespace PowerDisplay.Cli.Output;

/// <summary>
/// Machine-readable JSON output. Uses the source-generated
/// <see cref="CliJsonContext"/> so AOT keeps the type metadata.
/// </summary>
public sealed class JsonCliOutput : ICliOutput
{
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly bool _quiet;

    public JsonCliOutput(bool quiet = false)
        : this(Console.Out, Console.Error, quiet)
    {
    }

    public JsonCliOutput(TextWriter stdout, TextWriter stderr, bool quiet = false)
    {
        _stdout = stdout;
        _stderr = stderr;
        _quiet = quiet;
    }

    public void WriteListResult(CliListResult result)
        => _stdout.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.CliListResult));

    public void WriteSetResult(CliSetResult result)
        => _stdout.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.CliSetResult));

    public void WriteGetResult(CliGetResult result)
        => _stdout.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.CliGetResult));

    public void WriteCapabilitiesResult(CliCapabilitiesResult result)
        => _stdout.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.CliCapabilitiesResult));

    public void WriteProfileListResult(CliProfileListResult result)
        => _stdout.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.CliProfileListResult));

    public void WriteApplyProfileResult(CliApplyProfileResult result)
        => _stdout.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.CliApplyProfileResult));

    public void WriteError(CliErrorResult result)
        => _stderr.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.CliErrorResult));

    public void WriteWarning(string message)
    {
        if (!_quiet)
        {
            _stderr.WriteLine(message);
        }
    }
}
