// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace PowerDisplay.Contracts;

public sealed class CliProfileListResult
{
    public bool Ok { get; init; } = true;

    public string Version { get; init; } = CliSchema.Version;

    public string Command { get; init; } = "profiles";

    public IReadOnlyList<CliProfileInfo> Profiles { get; init; } = [];
}
