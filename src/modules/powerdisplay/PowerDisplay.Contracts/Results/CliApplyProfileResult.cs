// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace PowerDisplay.Contracts;

public sealed class CliApplyProfileResult
{
    /// <summary>
    /// True when every requested setting that the hardware supports applied successfully. Unsupported
    /// settings are skipped without failing; a hardware-failure or out-of-range value sets this false
    /// (and the process exit code reflects it).
    /// </summary>
    public bool Ok { get; init; } = true;

    public string Version { get; init; } = CliSchema.Version;

    public string Command { get; init; } = "apply-profile";

    public string Profile { get; init; } = string.Empty;

    public IReadOnlyList<CliProfileMonitorOutcome> Monitors { get; init; } = [];
}
