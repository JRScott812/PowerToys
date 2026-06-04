// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace PowerDisplay.Cli.Output;

/// <summary>
/// Stable schema version stamped onto every JSON result envelope so long-lived
/// consumers can detect format evolution. Bump the minor on additive changes,
/// the major on breaking changes.
/// </summary>
public static class CliSchema
{
    public const string Version = "1.0";
}
