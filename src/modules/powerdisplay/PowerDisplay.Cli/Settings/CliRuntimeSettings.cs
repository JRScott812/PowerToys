// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace PowerDisplay.Cli.Settings;

/// <summary>
/// PowerDisplay settings the CLI honours so it behaves like the GUI: the
/// max-compatibility toggle and the set of monitor ids the user hid.
/// </summary>
public sealed record CliRuntimeSettings(bool MaxCompatibilityMode, IReadOnlySet<string> HiddenMonitorIds)
{
    public static CliRuntimeSettings Default { get; } =
        new(false, new HashSet<string>());
}
