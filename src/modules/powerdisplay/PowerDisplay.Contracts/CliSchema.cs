// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace PowerDisplay.Contracts;

/// <summary>
/// Stable schema version stamped onto IPC response envelopes so the CLI and the
/// PowerDisplay app can detect format drift across versions.
/// </summary>
public static class CliSchema
{
    public const string Version = "1.0";
}
