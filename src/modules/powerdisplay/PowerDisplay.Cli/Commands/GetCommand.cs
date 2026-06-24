// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace PowerDisplay.Cli.Commands;

public static class GetCommand
{
    /// <summary>
    /// Canonical setting names accepted by <c>--setting</c>. The same identifiers
    /// are used in <see cref="PowerDisplay.Contracts.CliSettingValue.Setting"/> so JSON consumers can
    /// switch on them.
    /// </summary>
    public static readonly string[] AllSettingNames =
    [
        "brightness",
        "contrast",
        "volume",
        "color-temperature",
        "input-source",
        "power-state",
        "orientation",
    ];
}
